using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetByIdTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TaskPlannerDayTemplateResponse entity, CancellationToken ct) => Task.FromResult(true);
}