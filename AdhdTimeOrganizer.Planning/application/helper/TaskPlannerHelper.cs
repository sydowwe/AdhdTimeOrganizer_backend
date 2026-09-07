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

    // Assumes same-day, non-wrapping intervals (StartTime < EndTime). Every planner-task request validator
    // rejects EndTime <= StartTime, but there is no CHECK constraint and rows predating that rule may still
    // carry a negative span — this reads one as a same-day negative duration and simply reports no overlap,
    // which is why PlannerTemplateApplier screens for it (IsCarvable) instead of trusting this.
    public static bool TasksOverlap(this PlannerTask task, TimeOnly start2, TimeOnly end2) => task.StartTime < end2 && task.EndTime > start2;
}