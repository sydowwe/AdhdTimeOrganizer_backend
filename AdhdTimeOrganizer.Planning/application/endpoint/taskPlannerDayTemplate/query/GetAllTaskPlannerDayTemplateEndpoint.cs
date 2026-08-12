using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetAllTaskPlannerDayTemplateEndpoint(DbContext dbContext)
    : BaseGetAllEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse>(dbContext)
{
    protected override IQueryable<TaskPlannerDayTemplate> Sort(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.OrderByDescending(t => t.LastUsedAt).ThenBy(t => t.Name);
    }
}