using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetAllTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseGetAllEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse>(dbContext)
{
    protected override IQueryable<TaskPlannerDayTemplate> Sort(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.OrderByDescending(t => t.LastUsedAt).ThenBy(t => t.Name);
    }
}