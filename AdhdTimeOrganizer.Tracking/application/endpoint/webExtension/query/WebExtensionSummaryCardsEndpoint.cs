using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionSummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones) : Endpoint<SummaryCardsRequest, List<DomainSummaryDto>>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/summary-cards");
        Validator<TopDomainsValidator>();
        Summary(s =>
        {
            s.Summary = "Get top domain summary cards with baseline comparison";
            s.Description = "Returns summary statistics for top domains with baseline comparison against a selected reference period";
            s.Response<List<DomainSummaryDto>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(SummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // The requested span, as one time-of-day window per calendar day in it
        var timeZone = await timeZones.GetAsync(userId, ct);
        var windows = req.ToDailyWindows(timeZone);

        // 1. Get current period data
        var currentPeriodData = await GetPeriodData(userId, windows, ct);

        // 3. Calculate baseline period range, off the first instant of the span
        var (baselineFrom, baselineTo, baselineDays) =
            CalculateBaselineRange(windows.EnvelopeFrom, req.Baseline);

        // 4. Get baseline data, scaled from a mean day up to the length of the span
        var baselineData = await GetBaselineAverages(
            userId, baselineFrom, baselineTo, baselineDays, windows.DayCount, req.Baseline, timeZone, ct);

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
            // Original behavior: return top N
            filteredDomains = allDomains.Take(req.TopN.Value);
        else
            // Default: return top 5
            filteredDomains = allDomains.Take(5);

        // 7. Build response with comparisons
        var response = filteredDomains.Select(d => BuildDomainSummary(d, baselineData)).ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// One range predicate over the span's outer envelope, then the gaps between the daily windows are
    /// dropped in memory. Asking the database for each day separately would be N round trips over a
    /// partitioned ledger; asking it for the envelope alone would count every night the user's window
    /// excludes.
    /// </summary>
    private async Task<List<WebExtensionActivityEntry>> GetPeriodData(
        long userId,
        DailyWindowSet windows,
        CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<WebExtensionActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        return windows.Restrict(rows, x => x.WindowStart);
    }

    private (DateTime from, DateTime to, int days) CalculateBaselineRange(
        DateTime currentFrom,
        BaselineType baseline)
    {
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
                8 // 8 same weekdays
            ),

            BaselineType.AllTime => (
                DateTime.MinValue, // Or user's first activity date
                currentFrom,
                -1 // Special flag: calculate actual days from data
            ),

            _ => (currentFrom.AddDays(-7), currentFrom, 7)
        };
    }

    /// <summary>
    /// The baseline is still "the average day over the lookback", but the card's own number is now a
    /// span total, so the mean day is multiplied by <paramref name="spanDays"/> before it is compared
    /// against one. Comparing a seven-day total against a one-day mean would report every week as up
    /// 600%. On a single-day request <paramref name="spanDays"/> is 1 and the arithmetic is the
    /// unchanged one.
    /// </summary>
    private async Task<Dictionary<string, BaselineStats>> GetBaselineAverages(
        long userId,
        DateTime from,
        DateTime to,
        int days,
        int spanDays,
        BaselineType baseline,
        TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        var query = db.Set<WebExtensionActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to);

        var data = await query.ToListAsync(ct);

        // Weekday is a question about the user's calendar, not UTC's: read off the stored instant, a
        // Tuesday 00:30 session in Bratislava counts as Monday and lands in the wrong baseline.
        if (baseline == BaselineType.SameWeekday)
        {
            var targetWeekday = WallClockZone.FromUtc(from, timeZone).DayOfWeek;
            data = data.Where(x => WallClockZone.FromUtc(x.WindowStart, timeZone).DayOfWeek == targetWeekday).ToList();
        }

        // Likewise the span of days the history covers — this is the divisor of every average below.
        if (days == -1 && data.Any())
        {
            var minDate = WallClockZone.FromUtc(data.Min(x => x.WindowStart), timeZone).Date;
            var maxDate = WallClockZone.FromUtc(data.Max(x => x.WindowStart), timeZone).Date;
            days = (int)(maxDate - minDate).TotalDays + 1;
        }

        if (days <= 0)
            days = 1; // Prevent division by zero

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
                    AverageActiveSeconds = ScaleToSpan(g.Sum(x => x.ActiveSeconds), days, spanDays),
                    AverageBackgroundSeconds = ScaleToSpan(g.Sum(x => x.BackgroundSeconds), days, spanDays)
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

            Active = currentData.ActiveSeconds > 0
                ? new ActivityStatDto
                {
                    Seconds = currentData.ActiveSeconds,
                    AverageSeconds = isNew ? null : baseline!.AverageActiveSeconds,
                    PercentChange = isNew || baseline!.AverageActiveSeconds == 0
                        ? null
                        : CalculatePercentChange(currentData.ActiveSeconds, baseline.AverageActiveSeconds)
                }
                : null,

            Background = currentData.BackgroundSeconds > 0
                ? new ActivityStatDto
                {
                    Seconds = currentData.BackgroundSeconds,
                    AverageSeconds = isNew ? null : baseline!.AverageBackgroundSeconds,
                    PercentChange = isNew || baseline!.AverageBackgroundSeconds == 0
                        ? null
                        : CalculatePercentChange(currentData.BackgroundSeconds, baseline.AverageBackgroundSeconds)
                }
                : null
        };
    }

    /// <summary>
    /// The mean day of the baseline, multiplied out to the length of the requested span. Multiplied
    /// before the divide so a short-but-busy baseline is not rounded away; <c>long</c> because a
    /// year-long span times a year of seconds overflows <c>int</c> on the way through.
    /// </summary>
    private static int ScaleToSpan(int baselineTotalSeconds, int baselineDays, int spanDays) =>
        (int)((long)baselineTotalSeconds * spanDays / baselineDays);

    private static double CalculatePercentChange(int current, int average)
    {
        if (average == 0)
            return current > 0 ? 100.0 : 0.0;
        return Math.Round((double)(current - average) / average * 100, 1);
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