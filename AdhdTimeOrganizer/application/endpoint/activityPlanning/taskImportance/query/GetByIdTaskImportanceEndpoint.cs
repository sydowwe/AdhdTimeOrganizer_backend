using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskImportance.query;

public class GetByIdTaskImportanceEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<TaskImportance, TaskImportanceResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TaskImportanceResponse entity, CancellationToken ct) => Task.FromResult(true);
}