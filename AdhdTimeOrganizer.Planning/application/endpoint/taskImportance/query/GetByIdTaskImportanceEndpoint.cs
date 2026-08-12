using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskImportance.query;

public class GetByIdTaskImportanceEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<TaskImportance, TaskImportanceResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TaskImportanceResponse entity, CancellationToken ct) => Task.FromResult(true);
}