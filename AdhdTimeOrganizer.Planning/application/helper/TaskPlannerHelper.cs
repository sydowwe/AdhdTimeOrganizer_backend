using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.helper;

public static class TaskPlannerHelper
{
    /// <summary>
    /// The date it currently is <b>for the user</b>, which is the only "today" a planner path may use.
    ///
    /// <para>A <c>Calendar.Date</c> is a wall-clock date the user chose, so the row that is "today" has to be
    /// picked on the user's own clock. Taking <c>DateOnly.FromDateTime(DateTime.UtcNow)</c> instead makes a
    /// Bratislava user's 01:15 land on yesterday's calendar row: the fan-out then updates the wrong day and
    /// leaves the right one open, with nothing in the logs. The read paths
    /// (<c>PlannerStreakReader</c>, <c>GetPlanVsActualTrendPlannerTaskEndpoint</c>) have always resolved the
    /// zone first; this exists so the write paths cannot answer the question differently.</para>
    /// </summary>
    public static DateOnly TodayIn(TimeZoneInfo timeZone)
        => TodayIn(timeZone, DateTime.UtcNow);

    /// <inheritdoc cref="TodayIn(TimeZoneInfo)"/>
    /// <param name="timeZone">The user's zone.</param>
    /// <param name="utcNow">The current instant, for a caller holding an injected <c>TimeProvider</c> (FO4's re-plan).</param>
    public static DateOnly TodayIn(TimeZoneInfo timeZone, DateTime utcNow)
        => DateOnly.FromDateTime(WallClockZone.FromUtc(utcNow, timeZone));

    /// <summary>
    /// The date formats a <c>/calendar/day-plan/{date}</c> route accepts. ISO first because that is what a client
    /// holding a local date already has; <c>dd-MM-yyyy</c> second only so these routes cannot be the ones that
    /// break when someone copies a URL over from the sibling <c>by-Date</c> route, which speaks that format alone.
    /// </summary>
    private static readonly string[] DayPlanRouteDateFormats = ["yyyy-MM-dd", "dd-MM-yyyy"];

    /// <summary>Parses the <c>{date}</c> segment of a <c>/calendar/day-plan/{date}…</c> route.</summary>
    public static bool TryParseDayPlanRouteDate(string? raw, out DateOnly date)
        => DateOnly.TryParseExact(raw, DayPlanRouteDateFormats, out date);

    /// <summary>
    /// The last day of a range-aggregate window that is <b>materially over</b>: <paramref name="until"/>, capped at
    /// the day before the user's today. A result before the range's <c>From</c> means no day qualifies — a range
    /// entirely in the future, or entirely today — and the caller answers with zeroes rather than failing.
    /// <para>
    /// Today is excluded however late in the day the request arrives. A partial today contributes tasks that
    /// have simply not come around yet — the trend would count every one as never happened, and the skip
    /// summary would count every one in a per-hour denominator it cannot yet have a numerator for. Over a range,
    /// dropping one day costs nothing; miscounting it costs the number its meaning.
    /// </para>
    /// <para>
    /// One implementation behind both range reads (<c>GetPlanVsActualTrendPlannerTaskEndpoint</c>,
    /// <c>GetSkipSummaryPlannerTaskEndpoint</c>) on purpose: the client renders the two side by side, both
    /// described as "this period", and they must mean the same days.
    /// </para>
    /// </summary>
    public static DateOnly LastDayThatIsOver(DateOnly until, TimeZoneInfo timeZone)
    {
        var today = TodayIn(timeZone);
        return until < today ? until : today.AddDays(-1);
    }


    public static IQueryable<PlannerTask> WithIncludes(this IQueryable<PlannerTask> query)
    {
        return query
            .Include(pt => pt.Importance)
            .Include(pt => pt.Activity)
            .ThenInclude(a => a.Role)
            .Include(pt => pt.Activity)
            .ThenInclude(a => a.Category);
    }

    /// <summary>
    /// The tasks whose span meets the time-of-day window <c>[from, until]</c> — the day planner's only read
    /// filter, and the template editor's. Written once here rather than per endpoint: it is eighteen lines of
    /// three-way overlap logic over two different entities, and a second copy is how the two views start
    /// disagreeing about which tasks a window contains.
    ///
    /// <para><b>A window with <c>until &lt;= from</c> wraps midnight</b> and is read as the union of
    /// <c>[from, 23:59:59]</c> and <c>[00:00, until]</c> — which makes <c>00:00 → 00:00</c> mean "the whole
    /// day", the window the home page sends.</para>
    ///
    /// <para><b>A task that itself wraps midnight matches every window.</b> No write path can create one any
    /// more (see the <c>EndTime &gt; StartTime</c> invariant), but there is no CHECK constraint and older rows
    /// may still carry the shape; showing such a row in every window is the deliberate choice over hiding it
    /// from all of them, since the day planner is the only place the user can see and fix it.</para>
    /// </summary>
    public static IQueryable<T> InTimeWindow<T>(this IQueryable<T> query, TimeOnly from, TimeOnly until)
        where T : BasePlannerTask
    {
        var windowWrapsAround = until <= from;

        return windowWrapsAround
            ? query.Where(task =>
                // Overlaps [from, 23:59:59]
                (task.StartTime <= new TimeOnly(23, 59, 59) && task.EndTime >= from) ||
                // Overlaps [00:00:00, until]
                (task.StartTime <= until && task.EndTime >= new TimeOnly(0, 0, 0)) ||
                task.EndTime < task.StartTime)
            : query.Where(task =>
                // Overlaps [from, until]
                (task.StartTime < until && task.EndTime > from) ||
                task.EndTime < task.StartTime);
    }

    // Assumes same-day, non-wrapping intervals (StartTime < EndTime). Every planner-task request validator
    // rejects EndTime <= StartTime, but there is no CHECK constraint and rows predating that rule may still
    // carry a negative span — this reads one as a same-day negative duration and simply reports no overlap,
    // which is why PlannerTemplateApplier screens for it (IsCarvable) instead of trusting this.
    public static bool TasksOverlap(this PlannerTask task, TimeOnly start2, TimeOnly end2) => task.StartTime < end2 && task.EndTime > start2;
}