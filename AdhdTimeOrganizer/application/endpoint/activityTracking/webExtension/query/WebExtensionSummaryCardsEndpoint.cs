using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.summaryCards;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionSummaryCardsEndpoint(AppDbContext db) : Endpoint<SummaryCardsRequest, List<DomainSummaryDto>>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/summary-cards");
        Validator<TopDomainsValidator>();
    }

    public override async Task HandleAsync(SummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // Construct DateTime from DateOnly and TimeDto
        var (from, to) = req.ToDateTimeRange();

        // 1. Get current period data
        var currentPeriodData = await GetPeriodData(userId, from, to, ct);

        var totalSeconds = currentPeriodData.Sum(x => x.ActiveSeconds + x.BackgroundSeconds);

        // 3. Calculate baseline period range
        var (baselineFrom, baselineTo, baselineDays) = CalculateBaselineRange(from, to, req.Baseline);

        // 4. Get baseline data (daily averages)
        var baselineData = await GetBaselineAverages(userId, baselineFrom, baselineTo, baselineDays, req.Baseline, ct);

        // 5. Get all domains by total time (active + background)
        var allDomains = currentPeriodData
            .GroupBy(x => x.Domain)
            .Select(g => new DomainTimeData
            {
                Domain = g.Key,
                ActiveSeconds = g.Sum(x => x.ActiveSeconds),
                BackgroundSeconds = g.Sum(x => x.BackgroundSeconds),
                TotalSeconds = g.Sum(x => x.ActiveSeconds + x.BackgroundSeconds)
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        // 6. Filter domains based on TopN or MinPercent
        IEnumerable<DomainTimeData> filteredDomains;

        if (req.TopN.HasValue)
        {
            // Original behavior: return top N
            filteredDomains = allDomains.Take(req.TopN.Value);
        }
        else
        {
            // Default: return top 5
            filteredDomains = allDomains.Take(5);
        }

        // 7. Build response with comparisons
        var response = filteredDomains.Select(d => BuildDomainSummary(d, baselineData)).ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private async Task<List<WebExtensionActivityEntry>> GetPeriodData(
        long userId,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        return await db.WebExtensionActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);
    }

    private (DateTime from, DateTime to, int days) CalculateBaselineRange(
        DateTime currentFrom,
        DateTime currentTo,
        BaselineType baseline)
    {
        var currentDays = (int)(currentTo - currentFrom).TotalDays;
        if (currentDays < 1) currentDays = 1;

        return baseline switch
        {
            BaselineType.Last7Days => (
                currentFrom.AddDays(-7),
                currentFrom,
                7
            ),

            BaselineType.Last30Days => (
                currentFrom.AddDays(-30),
                currentFrom,
                30
            ),

            BaselineType.SameWeekday => (
                // Go back 8 weeks, will filter to same weekday in query
                currentFrom.AddDays(-56),
                currentFrom,
                8  // 8 same weekdays
            ),

            BaselineType.AllTime => (
                DateTime.MinValue,  // Or user's first activity date
                currentFrom,
                -1  // Special flag: calculate actual days from data
            ),

            _ => (currentFrom.AddDays(-7), currentFrom, 7)
        };
    }

    private async Task<Dictionary<string, BaselineStats>> GetBaselineAverages(
        long userId,
        DateTime from,
        DateTime to,
        int days,
        BaselineType baseline,
        CancellationToken ct)
    {
        var query = db.WebExtensionActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to);

        var data = await query.ToListAsync(ct);

        // For SameWeekday, filter to matching weekdays in memory
        if (baseline == BaselineType.SameWeekday)
        {
            var targetWeekday = from.DayOfWeek;
            data = data.Where(x => x.WindowStart.DayOfWeek == targetWeekday).ToList();
        }

        // Calculate actual number of days if AllTime
        if (days == -1 && data.Any())
        {
            var minDate = data.Min(x => x.WindowStart).Date;
            var maxDate = data.Max(x => x.WindowStart).Date;
            days = (int)(maxDate - minDate).TotalDays + 1;
        }

        if (days <= 0) days = 1;  // Prevent division by zero

        // Group by domain and calculate daily averages
        return data
            .GroupBy(x => x.Domain)
            .ToDictionary(
                g => g.Key,
                g => new BaselineStats
                {
                    TotalActiveSeconds = g.Sum(x => x.ActiveSeconds),
                    TotalBackgroundSeconds = g.Sum(x => x.BackgroundSeconds),
                    Days = days,
                    AverageActiveSeconds = g.Sum(x => x.ActiveSeconds) / days,
                    AverageBackgroundSeconds = g.Sum(x => x.BackgroundSeconds) / days
                }
            );
    }

    private DomainSummaryDto BuildDomainSummary(
        DomainTimeData currentData,
        Dictionary<string, BaselineStats> baselineData)
    {
        var hasBaseline = baselineData.TryGetValue(currentData.Domain, out var baseline);
        var isNew = !hasBaseline;

        return new DomainSummaryDto
        {
            Domain = currentData.Domain,
            IsNew = isNew,

            Active = currentData.ActiveSeconds > 0 ? new ActivityStatDto
            {
                Seconds = currentData.ActiveSeconds,
                AverageSeconds = isNew ? null : baseline!.AverageActiveSeconds,
                PercentChange = isNew || baseline!.AverageActiveSeconds == 0
                    ? null
                    : CalculatePercentChange(currentData.ActiveSeconds, baseline.AverageActiveSeconds)
            } : null,

            Background = currentData.BackgroundSeconds > 0 ? new ActivityStatDto
            {
                Seconds = currentData.BackgroundSeconds,
                AverageSeconds = isNew ? null : baseline!.AverageBackgroundSeconds,
                PercentChange = isNew || baseline!.AverageBackgroundSeconds == 0
                    ? null
                    : CalculatePercentChange(currentData.BackgroundSeconds, baseline.AverageBackgroundSeconds)
            } : null
        };
    }

    private static double CalculatePercentChange(int current, int average)
    {
        if (average == 0) return current > 0 ? 100.0 : 0.0;
        return Math.Round(((double)(current - average) / average) * 100, 1);
    }
}

// Helper classes
internal class DomainTimeData
{
    public required string Domain { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds { get; set; }
}

internal class BaselineStats
{
    public int TotalActiveSeconds { get; set; }
    public int TotalBackgroundSeconds { get; set; }
    public int Days { get; set; }
    public int AverageActiveSeconds { get; set; }
    public int AverageBackgroundSeconds { get; set; }
}
