using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.application.service;
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

    /// <summary>
    /// The edit form behind a row of the detail list. The row's activity may since have been archived, and
    /// the response projects that activity through a required navigation — so without this the form answered
    /// 404 for a row the list itself offers. See <see cref="ActivityHistoryReadExtensions"/>.
    /// </summary>
    protected override string[] IgnoredQueryFilters => ActivityHistoryReadExtensions.ArchivedActivityFilter;
}