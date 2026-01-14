using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.dto.response.activityPlanning.taskPlannerDayTemplate;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.application.mapper.activityPlanning;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetFilterSortedPlannerTask(AppCommandDbContext dbContext, PlannerTaskMapper mapper)
    : BaseFilterEndpoint<PlannerTask, PlannerTaskResponse, PlannerTaskFilter, PlannerTaskMapper>(dbContext, mapper)
{
    protected override IQueryable<PlannerTask> ApplyCustomFiltering(IQueryable<PlannerTask> query, PlannerTaskFilter filter)
    {
        var from = new TimeOnly(filter.From.Hours, filter.From.Minutes);
        var until = new TimeOnly(filter.Until.Hours, filter.Until.Minutes);
        var filterWrapsAround = until <= from;

        query = query.Where(t => t.CalendarId == filter.CalendarId);

        if (filterWrapsAround)
        {
            // Range is [From, 23:59:59] OR [00:00:00, Until]
            query = query.Where(task =>
                // Task overlaps with [From, 23:59:59]
                (task.StartTime <= new TimeOnly(23, 59, 59) && task.EndTime >= from) ||
                // Task overlaps with [00:00:00, Until]
                (task.StartTime <= until && task.EndTime >= new TimeOnly(0, 0, 0)) ||
                // Task itself wraps around (starts before midnight, ends after)
                (task.EndTime < task.StartTime)
            );
        }
        else
        {
            // Standard range [From, Until]
            query = query.Where(task =>
                // Task overlaps with [From, Until]
                (task.StartTime < until && task.EndTime > from) ||
                // Task itself wraps around, so it must overlap with any range during the day
                (task.EndTime < task.StartTime)
            );
        }

        return query;
    }

    public override SortByRequest[] DefaultSortBy =>
    [
        new()
        {
            Key = "StartTime",
            IsDesc = false
        }
    ];

    protected override IQueryable<PlannerTask> WithIncludes(IQueryable<PlannerTask> query)
    {
        return query
            .Include(pt => pt.Importance)
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Role)
            .Include(pt => pt.Activity)
                .ThenInclude(a => a.Category);
    }
}