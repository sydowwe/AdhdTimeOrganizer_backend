using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dashboard;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.summary;

public class HistorySummaryPieChartEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistorySummaryPieChartRequest, HistoryPieChartResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/summary/pie-chart");
    }

    public override async Task HandleAsync(HistorySummaryPieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The user's days, not UTC's — see DateRangeDto.ToUtcRange.
        var (from, to) = req.ToUtcRange(await timeZones.GetAsync(userId, ct));

        var records = await db.Set<ActivityHistory>()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var response = HistoryPieChartBuilder.Build(records, req.GroupBy, req.MaxItems);

        await Send.ResponseAsync(response, cancellation: ct);
    }
}
