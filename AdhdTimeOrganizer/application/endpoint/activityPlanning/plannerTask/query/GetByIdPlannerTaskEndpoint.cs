using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.query;

public class GetByIdPlannerTaskEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<PlannerTask, PlannerTaskResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(PlannerTaskResponse entity, CancellationToken ct) => Task.FromResult(true);
}