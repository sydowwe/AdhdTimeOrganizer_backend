using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dashboard;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.detail;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.detail;

public class HistoryDetailSummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistoryDetailSummaryCardsRequest, HistorySummaryCardsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/detail/summary-cards");
    }

    public override async Task HandleAsync(HistoryDetailSummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange(await timeZones.GetAsync(userId, ct));
        var dayCount = (int)Math.Ceiling((to - from).TotalDays);

        var currentRecords = await db.Set<ActivityHistory>()
            .AsNoTracking()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var window = HistoryBaselineWindow.Resolve(
            from, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), req.Baseline);

        var baselineQuery = db.Set<ActivityHistory>()
            .AsNoTracking()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId);

        var baselineRecords = await window.ApplyRange(baselineQuery).ToListAsync(ct);

        var previousFrom = from.AddDays(-dayCount);

        var previousRecords = await db.Set<ActivityHistory>()
            .AsNoTracking()
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= previousFrom && ah.StartTimestamp < from)
            .ToListAsync(ct);

        var previousTotal = previousRecords.Sum(ah => (long)ah.Length.TotalSeconds);

        var response = HistorySummaryCardsBuilder.Build(
            currentRecords, baselineRecords, window, req.GroupBy, req.TopN, dayCount, previousTotal);

        await Send.ResponseAsync(response, cancellation: ct);
    }
}
