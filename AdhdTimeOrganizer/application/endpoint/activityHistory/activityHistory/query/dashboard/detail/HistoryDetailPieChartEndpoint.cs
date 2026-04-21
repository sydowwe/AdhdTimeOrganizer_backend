using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.detail;
using AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query.dashboard.detail;

public class HistoryDetailPieChartEndpoint(AppDbContext db) : Endpoint<HistoryDetailPieChartRequest, HistoryPieChartResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/detail/pie-chart");
    }

    public override async Task HandleAsync(HistoryDetailPieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var records = await db.ActivityHistories
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        var allGroups = records
            .GroupBy(ah => ResolveGroupKey(ah, req.GroupBy))
            .Select(g => new HistoryPieChartItem
            {
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
        const double minPercentThreshold = 1.0; // Always use 1% threshold
        var maxItems = req.MaxItems > 0 ? req.MaxItems : 20; // Use TopN from request, default to 20

        if (grandTotal > 0)
        {
            var threshold = grandTotal * minPercentThreshold / 100.0;

            var aboveThreshold = allGroups.Where(i => i.TotalSeconds >= threshold).ToList();
            var belowThreshold = allGroups.Where(i => i.TotalSeconds < threshold).ToList();

            // Take at most (maxItems - 1) items above threshold to leave room for "other"
            items = aboveThreshold.Take(maxItems - 1).ToList();

            // Collect items that didn't make it into the final list
            var remainingItems = new List<HistoryPieChartItem>();

            // Add items above threshold that didn't make the cut due to max limit
            if (aboveThreshold.Count > maxItems - 1)
            {
                remainingItems.AddRange(aboveThreshold.Skip(maxItems - 1));
            }

            // Add all items below threshold
            remainingItems.AddRange(belowThreshold);

            if (remainingItems.Count > 0)
            {
                items.Add(new HistoryPieChartItem
                {
                    Name = "_other",
                    TotalSeconds = remainingItems.Sum(i => i.TotalSeconds),
                    Color = "#999",
                    Entries = remainingItems.Sum(i => i.Entries)
                });
            }
        }
        else
        {
            // No data, return empty list
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

    private static (string Name, string? Color) ResolveGroupKey(ActivityHistory ah, HistoryGroupBy groupBy)
    {
        return groupBy switch
        {
            HistoryGroupBy.Activity => (ah.Activity.Name, null),
            HistoryGroupBy.Role => (ah.Activity.Role.Name, ah.Activity.Role.Color),
            HistoryGroupBy.Category => ah.Activity.Category != null
                ? (ah.Activity.Category.Name, ah.Activity.Category.Color)
                : ("Uncategorized", null),
            _ => (ah.Activity.Name, null)
        };
    }
}
