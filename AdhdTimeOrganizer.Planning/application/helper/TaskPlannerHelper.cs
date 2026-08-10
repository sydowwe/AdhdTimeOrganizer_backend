using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

namespace AdhdTimeOrganizer.Planning.application.helper;

public static class TaskPlannerHelper
{
    public static IQueryable<PlannerTask> WithIncludes(this IQueryable<PlannerTask> query)
    {
        return query
            .Include(pt => pt.Importance)
            .Include(pt => pt.Activity)
            .ThenInclude(a => a.Role)
            .Include(pt => pt.Activity)
            .ThenInclude(a => a.Category);
    }

    // Assumes same-day, non-wrapping intervals (StartTime < EndTime); every planner-task validator
    // rejects EndTime <= StartTime, so overnight tasks can't reach here.
    public static bool TasksOverlap(this PlannerTask task, TimeOnly start2, TimeOnly end2) => task.StartTime < end2 && task.EndTime > start2;
}