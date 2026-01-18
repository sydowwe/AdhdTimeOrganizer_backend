using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace AdhdTimeOrganizer.application.helper;

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

    public static bool TasksOverlap(this PlannerTask task, TimeOnly start2, TimeOnly end2)
    {
        return task.StartTime < end2 && task.EndTime > start2;
    }
}