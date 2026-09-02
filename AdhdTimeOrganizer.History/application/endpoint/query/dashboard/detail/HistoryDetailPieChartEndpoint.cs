using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dashboard;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.detail;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.detail;

public class HistoryDetailPieChartEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistoryDetailPieChartRequest, HistoryPieChartResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/detail/pie-chart");
    }

    public override async Task HandleAsync(HistoryDetailPieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange(await timeZones.GetAsync(userId, ct));

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
