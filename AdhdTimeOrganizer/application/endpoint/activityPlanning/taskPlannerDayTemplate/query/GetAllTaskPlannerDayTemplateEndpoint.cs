using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetAllTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseGetAllEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse>(dbContext)
{
    protected override IQueryable<TaskPlannerDayTemplate> Sort(IQueryable<TaskPlannerDayTemplate> query)
        => query.OrderByDescending(t => t.LastUsedAt).ThenBy(t => t.Name);
}