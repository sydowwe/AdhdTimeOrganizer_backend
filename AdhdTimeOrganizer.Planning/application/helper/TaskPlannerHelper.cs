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

    // Assumes same-day, non-wrapping intervals (StartTime < EndTime). Every planner-task request validator
    // rejects EndTime <= StartTime, but there is no CHECK constraint and rows predating that rule may still
    // carry a negative span — this reads one as a same-day negative duration and simply reports no overlap,
    // which is why PlannerTemplateApplier screens for it (IsCarvable) instead of trusting this.
    public static bool TasksOverlap(this PlannerTask task, TimeOnly start2, TimeOnly end2) => task.StartTime < end2 && task.EndTime > start2;
}