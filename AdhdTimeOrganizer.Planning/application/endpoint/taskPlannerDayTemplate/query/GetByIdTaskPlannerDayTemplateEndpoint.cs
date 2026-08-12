using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetByIdTaskPlannerDayTemplateEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TaskPlannerDayTemplateResponse entity, CancellationToken ct) => Task.FromResult(true);
}