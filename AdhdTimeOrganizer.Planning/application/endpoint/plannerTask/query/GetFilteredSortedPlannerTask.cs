using AdhdTimeOrganizer.Planning.application.dto.filter;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.plannerTask.query;

public class FilterPlannerTaskEndpoint(DbContext dbContext)
    : BaseFilterEndpoint<PlannerTask, PlannerTaskResponse, PlannerTaskFilter>(dbContext)
{
    protected override IQueryable<PlannerTask> ApplyCustomFiltering(IQueryable<PlannerTask> query, PlannerTaskFilter filter)
    {
        // The window predicate lives on TaskPlannerHelper, shared verbatim with FilterTemplatePlannerTaskEndpoint —
        // see InTimeWindow for what a wrapping window and a wrapping task each mean.
        return query
            .Where(t => t.CalendarId == filter.CalendarId)
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