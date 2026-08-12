using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.templatePlannerTask.query;

public class FilterTemplatePlannerTaskEndpoint(DbContext dbContext)
    : BaseFilterEndpoint<TemplatePlannerTask, TemplatePlannerTaskResponse, TemplatePlannerTaskFilter>(dbContext)
{
    protected override IQueryable<TemplatePlannerTask> ApplyCustomFiltering(IQueryable<TemplatePlannerTask> query, TemplatePlannerTaskFilter filter)
    {
        var from = new TimeOnly(filter.From.Hours, filter.From.Minutes);
        var until = new TimeOnly(filter.Until.Hours, filter.Until.Minutes);
        var filterWrapsAround = until <= from;

        query = query.Where(t => t.TemplateId == filter.TemplateId);

        if (filterWrapsAround)
            // Range is [From, 23:59:59] OR [00:00:00, Until]
            query = query.Where(task =>
                // Task overlaps with [From, 23:59:59]
                (task.StartTime <= new TimeOnly(23, 59, 59) && task.EndTime >= from) ||
                // Task overlaps with [00:00:00, Until]
                (task.StartTime <= until && task.EndTime >= new TimeOnly(0, 0, 0)) ||
                // Task itself wraps around (starts before midnight, ends after)
                task.EndTime < task.StartTime
            );
        else
            // Standard range [From, Until]
            query = query.Where(task =>
                // Task overlaps with [From, Until]
                (task.StartTime < until && task.EndTime > from) ||
                // Task itself wraps around, so it must overlap with any range during the day
                task.EndTime < task.StartTime
            );

        return query;
    }

    public override SortByRequest[] AlwaysSortBy =>
    [
        new()
        {
            Key = "StartTime",
            IsDesc = false
        }
    ];
}