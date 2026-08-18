using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetAllTaskPlannerDayTemplateEndpoint(DbContext dbContext)
    : BaseGetAllEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse>(dbContext)
{
    /// <summary>
    /// Pinned templates lead, then the existing most-recently-used order. The client sorts pinned-first as
    /// well; doing it here too means a client that just renders the list in order gets the same thing.
    /// </summary>
    protected override IQueryable<TaskPlannerDayTemplate> Sort(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.LastUsedAt).ThenBy(t => t.Name);
    }
}