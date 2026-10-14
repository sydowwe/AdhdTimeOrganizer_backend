using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.timeAway.query;

public class GetByIdTimeAwayEndpoint(DbContext dbContext)
    : BaseGetByIdEndpoint<TimeAway, TimeAwayResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(TimeAwayResponse entity, CancellationToken ct) => Task.FromResult(true);
}
