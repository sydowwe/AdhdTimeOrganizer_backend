using AdhdTimeOrganizer.application.dto.filter;
using AdhdTimeOrganizer.application.dto.request.generic;
using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.read.pageFilterSort;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using TemplatePlannerTaskMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TemplatePlannerTaskMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.query;

public class GetFilterSortedTemplatePlannerTask(AppCommandDbContext dbContext, TemplatePlannerTaskMapper mapper)
    : BaseFilterEndpoint<TemplatePlannerTask, TemplatePlannerTaskResponse, TemplatePlannerTaskFilter, TemplatePlannerTaskMapper>(dbContext, mapper)
{
    protected override IQueryable<TemplatePlannerTask> ApplyCustomFiltering(IQueryable<TemplatePlannerTask> query, TemplatePlannerTaskFilter filter)
    {
        var from = new TimeOnly(filter.From.Hours, filter.From.Minutes);
        var until = new TimeOnly(filter.Until.Hours, filter.Until.Minutes);
        var filterWrapsAround = until <= from;

        query = query.Where(t => t.TemplateId == filter.TemplateId);

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

    protected override IQueryable<TemplatePlannerTask> WithIncludes(IQueryable<TemplatePlannerTask> query)
    {
        return query.Include(t => t.Activity);
    }
}