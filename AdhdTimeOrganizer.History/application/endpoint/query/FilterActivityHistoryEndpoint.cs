using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.application.dto.filter.history;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.generic;
using Sydowwe.Framework.application.endpoint.@base.read.pageFilterSort;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query;

public class FilterActivityHistoryEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones)
    : BaseFilterEndpoint<ActivityHistory, ActivityHistoryResponse, ActivityHistoryDetailFilter>(dbContext)
{
    public override SortByRequest[] AlwaysSortBy => [new("StartTimestamp", false), new("EndTimestamp", false)];

    // ApplyCustomFiltering is synchronous by contract, and resolving the caller's zone is a database read.
    // The base calls ApplyUserScoping first and awaits it, so that is where the zone is fetched; this field
    // is only ever read on the line after it is written, within one request.
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    /// <summary>
    /// Also the one hook this base offers before the projection runs, which is where the archived lift has to
    /// go: <see cref="ActivityHistoryResponse"/> projects the activity through a required navigation, so
    /// without it every session logged against an activity the user has since archived left this list —
    /// while the charts above it went on counting the same rows. See
    /// <see cref="ActivityHistoryReadExtensions"/>.
    /// </summary>
    protected override async Task<IQueryable<ActivityHistory>> ApplyUserScoping(
        IQueryable<ActivityHistory> query, long userId, CancellationToken ct = default)
    {
        _timeZone = await timeZones.GetAsync(userId, ct);
        return query.IncludingArchivedActivities();
    }

    protected override IQueryable<ActivityHistory> ApplyCustomFiltering(IQueryable<ActivityHistory> query, ActivityHistoryDetailFilter filter)
    {
        var (from, to) = filter.ToDateTimeRange(_timeZone);

        // Half-open on StartTimestamp, exactly as every dashboard, the calendar and both seams select: a
        // record starting inside the window contributes, tail included, however far past `to` it runs.
        // Bounding the upper end on EndTimestamp instead would drop long sessions from this list while
        // they still counted in the charts above it.
        query = query.Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to);

        return query;
    }
}
