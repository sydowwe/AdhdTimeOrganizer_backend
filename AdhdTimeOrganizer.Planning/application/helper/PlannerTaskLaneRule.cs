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
/// Only a write that <b>moves</b> a block is judged — a new block, or a changed day, start, end or kind. A day
/// that already holds three (from a bulk path, or from before this rule) must still let the user rename one of
/// them, tick it or write it a note.
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
    /// Whether <paramref name="task"/>, at its current day, span and kind, would be a third block at some minute.
    /// Reads only the blocks that meet its span; <paramref name="task"/> itself is left out by id (a new one has
    /// id 0, which no row has).
    /// </summary>
    public static async Task<bool> WouldExceedAsync(DbContext db, PlannerTask task, CancellationToken ct)
    {
        var others = await db.Set<PlannerTask>()
            .Where(t => t.CalendarId == task.CalendarId
                        && t.Id != task.Id
                        && t.IsBackground == task.IsBackground
                        && t.StartTime < task.EndTime
                        && t.EndTime > task.StartTime)
            .Select(t => new { t.StartTime, t.EndTime })
            .ToListAsync(ct);

        return ExceedsLaneLimit(task.StartTime, task.EndTime, others.Select(o => (o.StartTime, o.EndTime)));
    }

    /// <summary>
    /// A new block, or a tracked one whose day, start, end or kind differs from what was loaded. Compares the
    /// original and current values directly rather than trusting <c>IsModified</c>, which a full update's
    /// <c>Update()</c> would set on every column anyway.
    /// </summary>
    public static bool PlacementChanged(DbContext db, PlannerTask task)
    {
        var entry = db.Entry(task);
        if (entry.State is EntityState.Detached or EntityState.Added)
            return true;

        return Changed(entry.Property(t => t.CalendarId))
               || Changed(entry.Property(t => t.StartTime))
               || Changed(entry.Property(t => t.EndTime))
               || Changed(entry.Property(t => t.IsBackground));

        static bool Changed<T>(Microsoft.EntityFrameworkCore.ChangeTracking.PropertyEntry<PlannerTask, T> property) =>
            !EqualityComparer<T>.Default.Equals(property.OriginalValue, property.CurrentValue);
    }
}
