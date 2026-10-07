using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using Sydowwe.Framework.domain.helper;
using Calendar = AdhdTimeOrganizer.Planning.domain.model.entity.Calendar;

namespace AdhdTimeOrganizer.Planning.application.helper;

/// <summary>
/// FO4 — "re-plan the rest of today": turns one loaded day into a plan, and the plan into the token that lets
/// <c>POST …/replan/apply</c> prove it is applying exactly what the preview showed. The three FO4 routes share it,
/// so the preview, the apply and the next-free-slot read cannot disagree about what a day holds.
/// <list type="bullet">
/// <item><b>What moves.</b> Every <c>NotStarted</c> foreground block of the day whose activity is not
/// <c>IsUnavoidable</c> — missed, late or upcoming (<see cref="PlannerTaskMissedRule"/>). An upcoming block that
/// starts at or after the window's end (planned past bed time on purpose) is left where it is.</item>
/// <item><b>What stays and holds its time.</b> Everything else that is not skipped: <c>InProgress</c>,
/// <c>OnHold</c>, <c>Completed</c>, and any block of an unavoidable activity — missed or not. Its planned slot is
/// what it holds.</item>
/// <item><b>Background blocks neither move nor hold time</b>, the reading the day view's own conflict check takes
/// (<c>useDayPlannerCommon.checkConflict</c> skips them): they run alongside the day. <c>Cancelled</c> blocks hold
/// nothing.</item>
/// <item><b>The window</b> runs from <i>packed-from</i> — the user's now, rounded up to the whole minute — to bed
/// time (<see cref="DayPacking.WindowEnd"/>: 00:00, or any bed time after midnight, means the end of the day).</item>
/// <item><b>The order</b> is the blocks' current one (start, end, id). A missed or late block may start from
/// packed-from; an upcoming one never earlier than it is planned, and it may keep its own slot past bed time. The
/// rest is <see cref="DayPacking.Pack"/>: first fit, optional blocks give way first, lengths kept.</item>
/// <item><b>Nothing slipped, nothing to do.</b> With no missed or late block the plan is empty, even if upcoming
/// blocks overlap — the re-plan is an answer to a slip, not a tidy-up of the day.</item>
/// </list>
/// <para>
/// <b>Everything is derived from packed-from, never from the clock.</b> The apply recomputes the plan from the
/// token's packed-from against the day as it now stands, so a preview applied a minute later still means the
/// same placements — and a day that changed underneath it no longer produces the same token.
/// </para>
/// </summary>
public static class DayReplanPlanner
{
    /// <summary>
    /// How long a preview may be applied for. Past it the first placement may already be well behind the user, so
    /// the apply answers 409 and the client asks for a fresh one.
    /// </summary>
    public static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The token's hash part, in hex characters.</summary>
    private const int HashLength = 24;

    /// <summary>One block the plan moves within the day.</summary>
    public sealed record Move(long TaskId, PlannerTaskSlipState State, TimeOnly ToStart, TimeOnly ToEnd);

    /// <summary>One block the plan has no room for.</summary>
    public sealed record Unfit(long TaskId, PlannerTaskSlipState State, bool IsOptional);

    /// <summary>
    /// A computed plan. <see cref="Moves"/> holds only blocks whose slot changes, ordered by their new start;
    /// <see cref="WontFit"/> is optional first, then in the day's order.
    /// </summary>
    public sealed record Result(int PackedFrom, int WindowEnd, string Token, IReadOnlyList<Move> Moves, IReadOnlyList<Unfit> WontFit);

    /// <summary>The minute a re-plan asked for at <paramref name="utcNow"/> packs from: the user's wall clock, rounded up.</summary>
    public static int PackedFromNow(DateTime utcNow, TimeZoneInfo timeZone) =>
        DayPacking.CeilingMinuteOf(TimeOnly.FromDateTime(WallClockZone.FromUtc(utcNow, timeZone)));

    /// <summary>The UTC instant <paramref name="minute"/> (0 … 1440) names on <paramref name="date"/> in the user's zone.</summary>
    public static DateTime InstantOf(DateOnly date, int minute, TimeZoneInfo timeZone) =>
        WallClockZone.ToUtc(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute), timeZone);

    /// <summary>
    /// The user's calendar row for <paramref name="date"/> and every task on it, with the activity (for
    /// <c>IsUnavoidable</c>) and the importance (for <c>IsOptional</c>). Tracked when the caller is about to write.
    /// <para>
    /// Scoped by <c>UserId</c> by hand as well as through the host's global filter — this slice compiles against a
    /// plain <c>DbContext</c> and cannot see that filter. A task whose activity is archived drops out here exactly as
    /// it drops out of <c>GET /calendar/day-plan</c> (the archived filter reaches it through the required
    /// navigation), so a re-plan sees the same day the user does.
    /// </para>
    /// </summary>
    public static async Task<(Calendar? Calendar, List<PlannerTask> Tasks)> LoadDayAsync(
        DbContext dbContext, long userId, DateOnly date, bool track, CancellationToken ct)
    {
        var calendar = await dbContext.Set<Calendar>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Date == date, ct);
        if (calendar is null)
            return (null, []);

        var query = dbContext.Set<PlannerTask>()
            .Include(t => t.Activity)
            .Include(t => t.Importance)
            .Where(t => t.UserId == userId && t.CalendarId == calendar.Id);
        if (!track)
            query = query.AsNoTracking();

        return (calendar, await query.ToListAsync(ct));
    }

    /// <summary>
    /// The stretch of the day a block holds when it is <b>not</b> being placed: nothing for a background or skipped
    /// block, its planned slot otherwise. The next-free-slot read treats every block this way, since nothing moves
    /// there.
    /// </summary>
    public static DayPacking.Span? Holds(PlannerTask task) =>
        task.IsBackground || task.Status == PlannerTaskStatus.Cancelled
            ? null
            : DayPacking.Occupancy(task.StartTime, task.EndTime);

    /// <summary>The plan for <paramref name="date"/> (the user's today), packed from <paramref name="packedFrom"/>.</summary>
    public static Result Compute(Calendar? calendar, IReadOnlyList<PlannerTask> tasks, DateOnly date, int packedFrom)
    {
        var windowEnd = DayPacking.WindowEnd(
            calendar?.WakeUpTime ?? CalendarDayFactory.DefaultWakeUpTime,
            calendar?.BedTime ?? CalendarDayFactory.DefaultBedTime);
        var token = TokenFor(packedFrom, calendar, tasks);
        var userNow = date.ToDateTime(TimeOnly.MinValue).AddMinutes(packedFrom);

        var movable = new List<(PlannerTask Task, PlannerTaskSlipState State)>();
        var occupied = new List<DayPacking.Span>();
        foreach (var task in tasks)
        {
            if (MovableState(task, date, userNow, windowEnd) is { } state)
                movable.Add((task, state));
            else if (Holds(task) is { } held)
                occupied.Add(held);
        }

        if (!movable.Any(m => m.State is PlannerTaskSlipState.Late or PlannerTaskSlipState.Missed))
            return new Result(packedFrom, windowEnd, token, [], []);

        var ordered = movable
            .OrderBy(m => m.Task.StartTime).ThenBy(m => m.Task.EndTime).ThenBy(m => m.Task.Id)
            .ToList();
        var items = ordered.Select(m =>
        {
            var current = DayPacking.Occupancy(m.Task.StartTime, m.Task.EndTime);
            var upcoming = m.State == PlannerTaskSlipState.Upcoming;
            return new DayPacking.Item(
                m.Task.Id,
                current.Length,
                EarliestStart: upcoming ? current.Start : packedFrom,
                LatestEnd: upcoming ? Math.Min(Math.Max(windowEnd, current.End), DayPacking.LastEndMinute) : windowEnd,
                m.Task.IsOptional);
        }).ToList();

        var packed = DayPacking.Pack(occupied, items);

        var moves = new List<Move>();
        foreach (var (task, state) in ordered)
        {
            // A block placed back onto its own slot is not a move.
            if (packed.Placed.TryGetValue(task.Id, out var to) && to != DayPacking.Occupancy(task.StartTime, task.EndTime))
                moves.Add(new Move(task.Id, state, DayPacking.TimeOf(to.Start), DayPacking.TimeOf(to.End)));
        }
        // Placements never overlap and are never empty, so their starts are distinct and the order is total.
        moves.Sort((a, b) => a.ToStart.CompareTo(b.ToStart));

        var byId = ordered.ToDictionary(m => m.Task.Id);
        var wontFit = packed.WontFit
            .Select(id => new Unfit(id, byId[id].State, byId[id].Task.IsOptional))
            .ToList();

        return new Result(packedFrom, windowEnd, token, moves, wontFit);
    }

    /// <summary>The packed-from minute a token carries, if it is a well-formed token at all.</summary>
    public static bool TryReadPackedFrom(string? token, out int packedFrom)
    {
        packedFrom = 0;
        if (string.IsNullOrEmpty(token))
            return false;

        var dot = token.IndexOf('.');
        return dot > 0
               && token.Length - dot - 1 == HashLength
               && int.TryParse(token.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out packedFrom)
               && packedFrom is >= 0 and <= DayPacking.MinutesPerDay;
    }

    /// <summary>
    /// Whether a block is placed by the re-plan, and as what. <c>null</c> when it is not: begun, closed or
    /// background (the missed rule says so), unavoidable, a span the arithmetic cannot reason about (end at or
    /// before start), or an upcoming block that starts at or past the window's end.
    /// </summary>
    private static PlannerTaskSlipState? MovableState(PlannerTask task, DateOnly date, DateTime userNow, int windowEnd)
    {
        if (task.Activity.IsUnavoidable || task.EndTime <= task.StartTime)
            return null;

        var state = PlannerTaskMissedRule.Classify(task.Status, task.IsBackground, date, task.StartTime, task.EndTime, userNow);
        if (state == PlannerTaskSlipState.Upcoming && DayPacking.MinuteOf(task.StartTime) >= windowEnd)
            return null;

        return state;
    }

    /// <summary>
    /// <c>{packedFrom}.{hash}</c>, the hash over every fact the packing reads: the calendar's id and sleep window, and
    /// per task its id, status, span, background flag, optional flag and the activity's unavoidable flag. Nothing
    /// else — editing a block's notes after the preview does not make it stale, moving or adding one does.
    /// </summary>
    private static string TokenFor(int packedFrom, Calendar? calendar, IReadOnlyList<PlannerTask> tasks)
    {
        var facts = new StringBuilder();
        facts.Append(CultureInfo.InvariantCulture,
            $"{calendar?.Id ?? 0}|{calendar?.WakeUpTime.Ticks ?? -1}|{calendar?.BedTime.Ticks ?? -1}");
        foreach (var t in tasks.OrderBy(t => t.Id))
            facts.Append(CultureInfo.InvariantCulture,
                $";{t.Id},{(int)t.Status},{t.StartTime.Ticks},{t.EndTime.Ticks},{(t.IsBackground ? 1 : 0)},{(t.IsOptional ? 1 : 0)},{(t.Activity.IsUnavoidable ? 1 : 0)}");

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(facts.ToString())))[..HashLength];
        return string.Create(CultureInfo.InvariantCulture, $"{packedFrom}.{hash}");
    }
}
