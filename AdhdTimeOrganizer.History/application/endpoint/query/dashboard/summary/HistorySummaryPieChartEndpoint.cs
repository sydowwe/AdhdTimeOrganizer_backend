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

        var allGroups = records
            .GroupBy(ah => ah.ResolveGroupKey(req.GroupBy))
            .Select(g => new HistoryPieChartItem
            {
                GroupId = g.Key.Id,
                Name = g.Key.Name,
                TotalSeconds = g.Sum(ah => ah.Length.TotalSeconds),
                Color = g.Key.Color,
                Entries = g.Count()
            })
            .OrderByDescending(i => i.TotalSeconds)
            .ToList();

        var grandTotal = allGroups.Sum(i => i.TotalSeconds);
        var uniqueGroups = allGroups.Count;
        var totalEntries = records.Count;

        List<HistoryPieChartItem> items;
        const double minPercentThreshold = 1.0;
        var maxItems = req.MaxItems > 0 ? req.MaxItems : 20;

        if (grandTotal > 0)
        {
            var threshold = grandTotal * minPercentThreshold / 100.0;

            var aboveThreshold = allGroups.Where(i => i.TotalSeconds >= threshold).ToList();
            var belowThreshold = allGroups.Where(i => i.TotalSeconds < threshold).ToList();

            items = aboveThreshold.Take(maxItems - 1).ToList();

            var remainingItems = new List<HistoryPieChartItem>();

            if (aboveThreshold.Count > maxItems - 1)
                remainingItems.AddRange(aboveThreshold.Skip(maxItems - 1));

            remainingItems.AddRange(belowThreshold);

            if (remainingItems.Count > 0)
                items.Add(new HistoryPieChartItem
                {
                    // A roll-up of many groups, so it is no single entity: null id, keyed by name.
                    GroupId = null,
                    Name = "_other",
                    TotalSeconds = remainingItems.Sum(i => i.TotalSeconds),
                    Color = "#999",
                    Entries = remainingItems.Sum(i => i.Entries)
                });
        }
        else
        {
            items = new List<HistoryPieChartItem>();
        }

        var response = new HistoryPieChartResponse
        {
            Items = items,
            Totals = new HistoryPieTotals
            {
                TotalSeconds = grandTotal,
                TotalEntries = totalEntries,
                UniqueGroups = uniqueGroups
            }
        };

        await Send.ResponseAsync(response, cancellation: ct);
    }
}