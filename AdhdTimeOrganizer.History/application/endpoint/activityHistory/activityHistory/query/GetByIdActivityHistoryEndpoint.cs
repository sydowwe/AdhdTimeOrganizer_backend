using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query;

public class GetByIdActivityHistoryEndpoint(
    DbContext dbContext)
    : BaseGetByIdEndpoint<ActivityHistory, ActivityHistoryResponse>(dbContext)
{
    // Scoped by DbContext's global IEntityWithUser query filter, so a foreign row never projects.
    protected override Task<bool> AuthorizeAsync(ActivityHistoryResponse entity, CancellationToken ct) => Task.FromResult(true);
}