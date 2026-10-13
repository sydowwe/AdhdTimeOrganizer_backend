using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.application.helper;

/// <summary>
/// At most two planner tasks of one kind at any minute of a day: two foreground blocks side by side, and
/// separately two background ones. The SPA draws two as half-width lanes so the user can see both and decide
/// which to keep (its <c>utils/plannerLanes.ts</c>, the same rule); a third has nowhere to go on screen.
/// <para>
/// <b>Enforced on the three single-task writes only</b> — create, the full update and the span patch, through
/// <c>PlannerTaskLaneExtensions.EnsureWithinLanesAsync</c>. The bulk paths are not checked: both template
/// applies, copy-to-days, FO4's re-plan apply (whose packing already keeps to free time) and the carry-over.
/// Each has its own conflict story, and refusing a whole batch over one block would be worse than the SPA
/// drawing that block as a clash, which it does.
/// </para>
/// <para>
/// <b>FO6's materialiser places by this rule rather than being refused by it</b>
/// (<c>PlannerTemplateApplier.StageWithinLanes</c>): an occurrence that fits as a second block goes on whole, and
/// only one that would be a third falls back to the user's (non-destructive) conflict mode.
/// </para>
/// <para>
/// <b>A write is judged only on the minutes it newly covers</b> (<see cref="NewlyCovered(DbContext, PlannerTask)"/>).
/// A new block, or one moved to another day or to the other kind, covers its whole span anew. A block staying on
/// its day and kind newly covers only its new span minus its old one: an extension is judged on the added
/// stretch, a drag on the minutes it slides onto, and a shrink, a move inside the old span or a write that leaves
/// the span alone (a rename, a tick, a note) is never refused. So a block already in a three-way clash (left by
/// a bulk path, or from before this rule) can still be extended into free time — the SPA offers exactly that,
/// judging only the added stretch (<c>useWrapUpBlock.extendableMinutes</c>, home's <c>extendRoom.ts</c>) — while
/// the clash it already sits in is left as it is.
/// </para>
/// </summary>
public static class PlannerTaskLaneRule
{
    public const int MaxLanes = 2;

    public const string ErrorMessage =
        "Two tasks are already planned for part of this time; a third cannot be placed beside them.";

    /// <summary>
    /// Whether <c>[start, end)</c> would share some minute with <see cref="MaxLanes"/> or more of
    /// <paramref name="others"/>, which must be the same kind and the same day. Ends are exclusive: a block that
    /// ends at 10:00 and one that starts at 10:00 do not overlap. Spans with <c>end &lt;= start</c> overlap
    /// nothing, as in <see cref="TaskPlannerHelper.TasksOverlap"/>.
    /// </summary>
    public static bool ExceedsLaneLimit(TimeOnly start, TimeOnly end, IEnumerable<(TimeOnly Start, TimeOnly End)> others)
    {
        if (end <= start)
            return false;

        var events = new List<(TimeOnly At, int Delta)>();
        foreach (var (otherStart, otherEnd) in others)
        {
            if (otherEnd <= otherStart || otherStart >= end || otherEnd <= start)
                continue;
            events.Add((otherStart > start ? otherStart : start, 1));
            events.Add((otherEnd < end ? otherEnd : end, -1));
        }

        var depth = 0;
        var deepest = 0;
        // An end and a start on the same minute do not overlap, so ends go first.
        foreach (var (_, delta) in events.OrderBy(e => e.At).ThenBy(e => e.Delta))
        {
            depth += delta;
            deepest = Math.Max(deepest, depth);
        }

        return deepest + 1 > MaxLanes;
    }

    /// <summary>
    /// Whether any of <paramref name="stretches"/> would share some minute with <see cref="MaxLanes"/> or more of
    /// <paramref name="others"/> — the single-span <c>ExceedsLaneLimit</c> per stretch. No stretches (nothing newly
    /// covered) never exceeds.
    /// </summary>
    public static bool ExceedsLaneLimit(
        IEnumerable<(TimeOnly Start, TimeOnly End)> stretches, IReadOnlyCollection<(TimeOnly Start, TimeOnly End)> others) =>
        stretches.Any(s => ExceedsLaneLimit(s.Start, s.End, others));

    /// <summary>
    /// The minutes of <c>[start, end)</c> that <paramref name="before"/> did not cover: 0, 1 or 2 stretches, in
    /// order. <paramref name="before"/> is the block's old span on the <b>same day and kind</b>; pass <c>null</c>
    /// for a new block or one moved to another day or kind, whose whole span is new. An extension yields
    /// <c>[oldEnd, newEnd)</c>, a drag down <c>[max(oldEnd, newStart), newEnd)</c>, a drag up
    /// <c>[newStart, min(oldStart, newEnd))</c>, a growth at both ends two stretches, and a shrink or a move
    /// inside the old span nothing. A span with <c>end &lt;= start</c> covers nothing, as in <c>ExceedsLaneLimit</c>.
    /// </summary>
    public static IReadOnlyList<(TimeOnly Start, TimeOnly End)> NewlyCovered(
        (TimeOnly Start, TimeOnly End)? before, TimeOnly start, TimeOnly end)
    {
        if (end <= start)
            return [];
        if (before is not { } old || old.End <= old.Start)
            return [(start, end)];

        var stretches = new List<(TimeOnly Start, TimeOnly End)>(2);
        if (start < old.Start)
            stretches.Add((start, end < old.Start ? end : old.Start));
        if (end > old.End)
            stretches.Add((start > old.End ? start : old.End, end));
        return stretches;
    }

    /// <summary>
    /// What this write newly covers of <paramref name="task"/>'s span, read off the change tracker: the whole span
    /// for a new (or detached) block or one whose day or kind changed, otherwise the current span minus the span
    /// it was loaded with. Compares original and current values directly rather than trusting <c>IsModified</c>,
    /// which a full update's <c>Update()</c> would set on every column anyway.
    /// </summary>
    public static IReadOnlyList<(TimeOnly Start, TimeOnly End)> NewlyCovered(DbContext db, PlannerTask task)
    {
        var entry = db.Entry(task);
        (TimeOnly, TimeOnly)? before = null;
        if (entry.State is not (EntityState.Detached or EntityState.Added)
            && !Changed(entry.Property(t => t.CalendarId))
            && !Changed(entry.Property(t => t.IsBackground)))
            before = (entry.Property(t => t.StartTime).OriginalValue, entry.Property(t => t.EndTime).OriginalValue);

        return NewlyCovered(before, task.StartTime, task.EndTime);

        static bool Changed<T>(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<PlannerTask, T> property) =>
            !EqualityComparer<T>.Default.Equals(property.OriginalValue, property.CurrentValue);
    }

    /// <summary>
    /// Whether the minutes this write newly covers (<see cref="NewlyCovered(DbContext, PlannerTask)"/>) would make
    /// <paramref name="task"/> a third block of its kind at some minute of its day. Nothing newly covered is never
    /// refused and reads nothing. Reads only the same-kind blocks that meet the task's span; <paramref name="task"/>
    /// itself is left out by id (a new one has id 0, which no row has).
    /// </summary>
    public static async Task<bool> WouldExceedAsync(DbContext db, PlannerTask task, CancellationToken ct)
    {
        var stretches = NewlyCovered(db, task);
        if (stretches.Count == 0)
            return false;

        var others = await db.Set<PlannerTask>()
            .Where(t => t.CalendarId == task.CalendarId
                        && t.Id != task.Id
                        && t.IsBackground == task.IsBackground
                        && t.StartTime < task.EndTime
                        && t.EndTime > task.StartTime)
            .Select(t => new { t.StartTime, t.EndTime })
            .ToListAsync(ct);

        return ExceedsLaneLimit(stretches, others.Select(o => (o.StartTime, o.EndTime)).ToList());
    }
}
