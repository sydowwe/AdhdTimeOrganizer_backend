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
        => DateOnly.FromDateTime(WallClockZone.FromUtc(DateTime.UtcNow, timeZone));


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