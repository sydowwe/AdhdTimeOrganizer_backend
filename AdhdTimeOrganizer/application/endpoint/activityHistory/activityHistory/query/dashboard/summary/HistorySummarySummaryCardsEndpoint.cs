using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.activityHistory.dashboard.summary;
using AdhdTimeOrganizer.application.dto.response.activityHistory.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.activityHistory.query.dashboard.summary;

public class HistorySummarySummaryCardsEndpoint(AppDbContext db) : Endpoint<HistorySummarySummaryCardsRequest, HistorySummaryCardsResponse>
{
    public override void Configure()
    {
        Post("/activity-history/dashboard/summary/summary-cards");
    }

    public override async Task HandleAsync(HistorySummarySummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (fromDate, toDate) = req.ToDateRange();
        var from = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayCount = (int)Math.Ceiling((to - from).TotalDays);

        var currentRecords = await db.ActivityHistories
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= from && ah.StartTimestamp < to)
            .ToListAsync(ct);

        // Step 1: Top N groups in current period
        var topGroups = currentRecords
            .GroupBy(ah => ResolveGroupKey(ah, req.GroupBy))
            .Select(g => new
            {
                g.Key.Name,
                g.Key.Color,
                TotalSeconds = g.Sum(ah => (long)ah.Length.TotalSeconds),
                Records = g.ToList()
            })
            .OrderByDescending(g => g.TotalSeconds)
            .Take(req.TopN)
            .ToList();

        // Step 2: Compute baseline range
        var (baselineFrom, baselineTo, baselineWeekdayFilter) = ComputeBaselineRange(from, fromDate, toDate, req.Baseline);

        var baselineRecords = await db.ActivityHistories
            .Include(ah => ah.Activity).ThenInclude(a => a.Role)
            .Include(ah => ah.Activity).ThenInclude(a => a.Category)
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= baselineFrom && ah.StartTimestamp < from)
            .ToListAsync(ct);

        // For sameWeekday, filter to matching weekdays
        if (baselineWeekdayFilter != null)
        {
            baselineRecords = baselineRecords
                .Where(ah => baselineWeekdayFilter.Contains(ah.StartTimestamp.DayOfWeek))
                .ToList();
        }

        // Compute baseline days for daily average
        var baselineDays = ComputeBaselineDays(baselineFrom, from, req.Baseline, baselineRecords, baselineWeekdayFilter);

        // Step 3: Build cards
        var cards = topGroups.Select(g =>
        {
            var baselineTotal = baselineRecords
                .Where(ah => ResolveGroupKey(ah, req.GroupBy) == (g.Name, g.Color))
                .Sum(ah => (long)ah.Length.TotalSeconds);

            var baselineDailyAvg = baselineDays > 0 ? baselineTotal / baselineDays : 0;
            var currentDailyAvg = dayCount > 0 ? g.TotalSeconds / dayCount : g.TotalSeconds;

            var isNew = baselineTotal == 0 && g.TotalSeconds > 0;

            double? percentChange = null;
            if (!isNew && baselineDailyAvg > 0)
            {
                percentChange = Math.Round((double)(currentDailyAvg - baselineDailyAvg) / baselineDailyAvg * 100, 1);
            }

            return new HistorySummaryCard
            {
                Name = g.Name,
                Color = g.Color,
                TotalSeconds = g.TotalSeconds,
                AverageSeconds = baselineDailyAvg,
                PercentChange = percentChange,
                IsNew = isNew
            };
        }).ToList();

        // Step 4: Period comparison
        var periodLength = dayCount;
        var previousFrom = from.AddDays(-periodLength);
        var previousTo = from;

        var previousRecords = await db.ActivityHistories
            .Where(ah => ah.UserId == userId)
            .Where(ah => ah.StartTimestamp >= previousFrom && ah.StartTimestamp < previousTo)
            .ToListAsync(ct);

        var previousTotal = previousRecords.Sum(ah => (long)ah.Length.TotalSeconds);

        var currentTotal = currentRecords.Sum(ah => (long)ah.Length.TotalSeconds);

        double? periodPercentChange = null;
        if (previousTotal > 0)
        {
            periodPercentChange = Math.Round((double)(currentTotal - previousTotal) / previousTotal * 100, 1);
        }

        var response = new HistorySummaryCardsResponse
        {
            Cards = cards,
            PeriodComparison = new HistoryPeriodComparison
            {
                PreviousPeriodTotalSeconds = previousTotal,
                CurrentPeriodTotalSeconds = currentTotal,
                PercentChange = periodPercentChange
            }
        };

        await SendAsync(response, cancellation: ct);
    }

    private static (DateTime BaselineFrom, DateTime BaselineTo, HashSet<DayOfWeek>? WeekdayFilter) ComputeBaselineRange(
        DateTime from, DateOnly dateFrom, DateOnly dateTo, string baseline)
    {
        return baseline.ToLowerInvariant() switch
        {
            "last7days" => (from.AddDays(-7), from, null),
            "last30days" => (from.AddDays(-30), from, null),
            "sameweekday" => (
                from.AddDays(-56),
                from,
                GetWeekdaysInRange(dateFrom, dateTo)
            ),
            "alltime" => (DateTime.MinValue, from, null),
            _ => (from.AddDays(-7), from, null)
        };
    }

    private static HashSet<DayOfWeek> GetWeekdaysInRange(DateOnly dateFrom, DateOnly dateTo)
    {
        var weekdays = new HashSet<DayOfWeek>();
        var current = dateFrom;
        while (current <= dateTo)
        {
            weekdays.Add(current.DayOfWeek);
            current = current.AddDays(1);
        }
        return weekdays;
    }

    private static long ComputeBaselineDays(
        DateTime baselineFrom, DateTime currentFrom, string baseline,
        List<ActivityHistory> baselineRecords, HashSet<DayOfWeek>? weekdayFilter)
    {
        switch (baseline.ToLowerInvariant())
        {
            case "last7days":
                return 7;
            case "last30days":
                return 30;
            case "sameweekday":
                return 8 * (weekdayFilter?.Count ?? 1);
            case "alltime":
                if (baselineRecords.Count == 0) return 1;
                var minDate = baselineRecords.Min(ah => ah.StartTimestamp).Date;
                var maxDate = currentFrom.Date;
                var days = (long)(maxDate - minDate).TotalDays;
                return days > 0 ? days : 1;
            default:
                return 7;
        }
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
