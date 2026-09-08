using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.templatePlannerTask.query;

public class FilterTemplatePlannerTaskEndpoint(DbContext dbContext)
    : BaseFilterEndpoint<TemplatePlannerTask, TemplatePlannerTaskResponse, TemplatePlannerTaskFilter>(dbContext)
{
    protected override IQueryable<TemplatePlannerTask> ApplyCustomFiltering(IQueryable<TemplatePlannerTask> query, TemplatePlannerTaskFilter filter)
    {
        // The window predicate lives on TaskPlannerHelper, shared verbatim with FilterPlannerTaskEndpoint —
        // see InTimeWindow for what a wrapping window and a wrapping task each mean.
        return query
            .Where(t => t.TemplateId == filter.TemplateId)
            .InTimeWindow(
                new TimeOnly(filter.From.Hours, filter.From.Minutes),
                new TimeOnly(filter.Until.Hours, filter.Until.Minutes));
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