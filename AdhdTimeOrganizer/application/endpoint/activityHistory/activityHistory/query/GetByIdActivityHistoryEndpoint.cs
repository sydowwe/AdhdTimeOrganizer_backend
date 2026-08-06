using AdhdTimeOrganizer.application.dto.response.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query;

public class GetByIdActivityHistoryEndpoint(
    AppDbContext dbContext)
    : BaseGetByIdEndpoint<ActivityHistory, ActivityHistoryResponse>(dbContext)
{
    // Scoped by AppDbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityHistoryResponse entity, CancellationToken ct) => Task.FromResult(true);
}