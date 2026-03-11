using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.response.activityTracking.summaryCards;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query;

public class DesktopSummaryCardsEndpoint(AppDbContext db) : Endpoint<SummaryCardsRequest, List<DesktopProcessSummaryDto>>
{
    public override void Configure()
    {
        Post("/activity-tracking/desktop/summary-cards");
        Validator<TopDomainsValidator>();
    }

    public override async Task HandleAsync(SummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var currentPeriodData = await GetPeriodData(userId, from, to, ct);

        var (baselineFrom, baselineTo, baselineDays) = CalculateBaselineRange(from, to, req.Baseline);

        var baselineData = await GetBaselineAverages(userId, baselineFrom, baselineTo, baselineDays, req.Baseline, ct);

        var allProcesses = currentPeriodData
            .GroupBy(x => x.ProcessName)
            .Select(g => new DesktopProcessTimeData
            {
                ProcessName = g.Key,
                ProductName = g.Where(x => !string.IsNullOrEmpty(x.ProductName))
                    .Select(x => x.ProductName)
                    .FirstOrDefault() ?? g.Key,
                ActiveSeconds = g.Sum(x => x.ActiveSeconds),
                BackgroundSeconds = g.Sum(x => x.BackgroundSeconds),
                TotalSeconds = g.Sum(x => x.ActiveSeconds + x.BackgroundSeconds)
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        var filteredProcesses = req.TopN.HasValue
            ? allProcesses.Take(req.TopN.Value)
            : allProcesses.Take(5);

        var response = filteredProcesses.Select(p => BuildProcessSummary(p, baselineData)).ToList();

        await SendAsync(response, cancellation: ct);
    }

    private async Task<List<DesktopActivityEntry>> GetPeriodData(
        long userId, DateTime from, DateTime to, CancellationToken ct)
    {
        return await db.DesktopActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);
    }

    private static (DateTime from, DateTime to, int days) CalculateBaselineRange(
        DateTime currentFrom, DateTime currentTo, BaselineType baseline)
    {
        return baseline switch
        {
            BaselineType.Last7Days => (currentFrom.AddDays(-7), currentFrom, 7),
            BaselineType.Last30Days => (currentFrom.AddDays(-30), currentFrom, 30),
            BaselineType.SameWeekday => (currentFrom.AddDays(-56), currentFrom, 8),
            BaselineType.AllTime => (DateTime.MinValue, currentFrom, -1),
            _ => (currentFrom.AddDays(-7), currentFrom, 7)
        };
    }

    private async Task<Dictionary<string, DesktopBaselineStats>> GetBaselineAverages(
        long userId, DateTime from, DateTime to, int days, BaselineType baseline, CancellationToken ct)
    {
        var data = await db.DesktopActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        if (baseline == BaselineType.SameWeekday)
        {
            var targetWeekday = from.DayOfWeek;
            data = data.Where(x => x.WindowStart.DayOfWeek == targetWeekday).ToList();
        }

        if (days == -1 && data.Count > 0)
        {
            var minDate = data.Min(x => x.WindowStart).Date;
            var maxDate = data.Max(x => x.WindowStart).Date;
            days = (int)(maxDate - minDate).TotalDays + 1;
        }

        if (days <= 0) days = 1;

        return data
            .GroupBy(x => x.ProcessName)
            .ToDictionary(
                g => g.Key,
                g => new DesktopBaselineStats
                {
                    Days = days,
                    AverageActiveSeconds = g.Sum(x => x.ActiveSeconds) / days,
                    AverageBackgroundSeconds = g.Sum(x => x.BackgroundSeconds) / days
                }
            );
    }

    private static DesktopProcessSummaryDto BuildProcessSummary(
        DesktopProcessTimeData currentData,
        Dictionary<string, DesktopBaselineStats> baselineData)
    {
        var hasBaseline = baselineData.TryGetValue(currentData.ProcessName, out var baseline);
        var isNew = !hasBaseline;

        return new DesktopProcessSummaryDto
        {
            ProcessName = currentData.ProcessName,
            ProductName = currentData.ProductName,
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

internal class DesktopProcessTimeData
{
    public string ProcessName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds { get; set; }
}

internal class DesktopBaselineStats
{
    public int Days { get; set; }
    public int AverageActiveSeconds { get; set; }
    public int AverageBackgroundSeconds { get; set; }
}
