using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.History.application.dashboard;
using AdhdTimeOrganizer.History.application.dto.request.activityHistory.dashboard.summary;
using AdhdTimeOrganizer.History.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.History.application.endpoint.activityHistory.activityHistory.query.dashboard.summary;

public class HistorySummarySummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<HistorySummarySummaryCardsRequest, HistorySummaryCardsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/summary/summary-cards");
    }

    public override async Task HandleAsync(HistorySummarySummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The user's days, not UTC's — see DateRangeDto.ToUtcRange.
        var (fromDate, toDate) = req.ToDateRange();
        var (from, to) = req.ToUtcRange(await timeZones.GetAsync(userId, ct));
        var dayCount = (int)Math.Ceiling((to - from).TotalDays);

        var currentRecords = await db.Set<ActivityHistory>()
            .AsNoTracking()
            // Both grouped reads lift the archived concern; the previous-period sum below never joined
            // Activity and so always counted archived rows. Left as it was, this period dropped them and the
            // period it is compared against did not, which is a percentChange between two different rules.
            // See ActivityHistoryReadExtensions.
            .IncludingArchivedActivities()
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var window = HistoryBaselineWindow.Resolve(from, fromDate, toDate, req.Baseline);

        var baselineQuery = db.Set<ActivityHistory>()
            .AsNoTracking()
            // Both grouped reads lift the archived concern; the previous-period sum below never joined
            // Activity and so always counted archived rows. Left as it was, this period dropped them and the
            // period it is compared against did not, which is a percentChange between two different rules.
            // See ActivityHistoryReadExtensions.
            .IncludingArchivedActivities()
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
