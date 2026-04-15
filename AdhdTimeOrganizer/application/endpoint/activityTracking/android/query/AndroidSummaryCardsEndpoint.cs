using AdhdTimeOrganizer.application.dto.@enum;
using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.query;

public class AndroidSummaryCardsEndpoint(AppDbContext db) : Endpoint<SummaryCardsRequest, List<AndroidAppSummaryDto>>
{
    public override void Configure()
    {
        Post("/activity-tracking/android/summary-cards");
        Validator<TopDomainsValidator>();
    }

    public override async Task HandleAsync(SummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var currentData = await db.AndroidSessionDataEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
            .ToListAsync(ct);

        var (baselineFrom, baselineTo, baselineDays) = CalculateBaselineRange(from, to, req.Baseline);

        var baselineAverages = await GetBaselineAverages(userId, baselineFrom, baselineTo, baselineDays, req.Baseline, ct);

        var topN = req.TopN ?? 4;

        var topApps = currentData
            .GroupBy(x => x.AppLabel)
            .Select(g => new
            {
                AppLabel = g.Key,
                PackageName = g.First().PackageName,
                TotalSeconds = g.Sum(x => x.DurationSeconds)
            })
            .OrderByDescending(x => x.TotalSeconds)
            .Take(topN)
            .ToList();

        var response = topApps.Select(app =>
        {
            var hasBaseline = baselineAverages.TryGetValue(app.AppLabel, out var baselineAvg);
            var isNew = !hasBaseline;

            return new AndroidAppSummaryDto
            {
                PackageName = app.PackageName,
                AppLabel = app.AppLabel,
                IsNew = isNew,
                Stat = new AndroidActivityStatDto
                {
                    Seconds = app.TotalSeconds,
                    AverageSeconds = isNew ? null : baselineAvg,
                    PercentChange = isNew || baselineAvg == 0
                        ? null
                        : CalculatePercentChange(app.TotalSeconds, baselineAvg)
                }
            };
        }).ToList();

        await SendAsync(response, cancellation: ct);
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

    private async Task<Dictionary<string, long>> GetBaselineAverages(
        long userId, DateTime from, DateTime to, int days, BaselineType baseline, CancellationToken ct)
    {
        var data = await db.AndroidSessionDataEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
            .ToListAsync(ct);

        if (baseline == BaselineType.SameWeekday)
        {
            var targetWeekday = from.DayOfWeek;
            data = data.Where(x => x.SessionStartUtc.DayOfWeek == targetWeekday).ToList();
        }

        if (days == -1 && data.Count > 0)
        {
            var minDate = data.Min(x => x.SessionStartUtc).Date;
            var maxDate = data.Max(x => x.SessionStartUtc).Date;
            days = (int)(maxDate - minDate).TotalDays + 1;
        }

        if (days <= 0) days = 1;

        return data
            .GroupBy(x => x.AppLabel)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.DurationSeconds) / days
            );
    }

    private static double CalculatePercentChange(long current, long average)
    {
        if (average == 0) return current > 0 ? 100.0 : 0.0;
        return Math.Round(((double)(current - average) / average) * 100, 1);
    }
}
