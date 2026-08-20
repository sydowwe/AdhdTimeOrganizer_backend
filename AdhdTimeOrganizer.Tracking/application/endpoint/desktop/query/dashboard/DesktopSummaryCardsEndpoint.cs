using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopSummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones) : Endpoint<SummaryCardsRequest, List<DesktopProcessSummaryDto>>
{
    public override void Configure()
    {
        Post("/activity-tracking/desktop/summary-cards");
        Summary(s =>
        {
            s.Summary = "Get desktop process usage summary cards";
            s.Description = "Returns top N processes with current usage (active and background) and comparison against baseline period";
            s.Response<List<DesktopProcessSummaryDto>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<TopDomainsValidator>();
    }

    public override async Task HandleAsync(SummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var timeZone = await timeZones.GetAsync(userId, ct);
        var windows = req.ToDailyWindows(timeZone);

        var currentPeriodData = await GetPeriodData(userId, windows, ct);

        var (baselineFrom, baselineTo, baselineDays) =
            CalculateBaselineRange(windows.EnvelopeFrom, req.Baseline);

        var baselineData = await GetBaselineAverages(
            userId, baselineFrom, baselineTo, baselineDays, windows.DayCount, req.Baseline, timeZone, ct);

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

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// One range predicate over the span's outer envelope, then the gaps between the daily windows are
    /// dropped in memory. Asking the database for each day separately would be N round trips over a
    /// partitioned ledger; asking it for the envelope alone would count every night the user's window
    /// excludes.
    /// </summary>
    private async Task<List<DesktopActivityEntry>> GetPeriodData(
        long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<DesktopActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        return windows.Restrict(rows, x => x.WindowStart);
    }

    private static (DateTime from, DateTime to, int days) CalculateBaselineRange(
        DateTime currentFrom, BaselineType baseline)
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

    /// <summary>
    /// Both the weekday match and the all-time day span are calendar questions, so both are asked of the
    /// user's calendar rather than UTC's. Read straight off the stored instant, a session at half past
    /// midnight on a Tuesday in Bratislava counts as Monday, and the day span of a user's history could
    /// come out a day short — the divisor of every average on these cards.
    ///
    /// <para>The mean day is then multiplied by <paramref name="spanDays"/>, because the card's own
    /// number is a total over the requested span rather than over one day. Comparing a seven-day total
    /// against a one-day mean would report every week as up 600%. On a single-day request
    /// <paramref name="spanDays"/> is 1 and the arithmetic is the unchanged one.</para>
    /// </summary>
    private async Task<Dictionary<string, DesktopBaselineStats>> GetBaselineAverages(
        long userId, DateTime from, DateTime to, int days, int spanDays, BaselineType baseline, TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        var data = await db.Set<DesktopActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .ToListAsync(ct);

        if (baseline == BaselineType.SameWeekday)
        {
            var targetWeekday = WallClockZone.FromUtc(from, timeZone).DayOfWeek;
            data = data.Where(x => WallClockZone.FromUtc(x.WindowStart, timeZone).DayOfWeek == targetWeekday).ToList();
        }

        if (days == -1 && data.Count > 0)
        {
            var minDate = WallClockZone.FromUtc(data.Min(x => x.WindowStart), timeZone).Date;
            var maxDate = WallClockZone.FromUtc(data.Max(x => x.WindowStart), timeZone).Date;
            days = (int)(maxDate - minDate).TotalDays + 1;
        }

        if (days <= 0)
            days = 1;

        return data
            .GroupBy(x => x.ProcessName)
            .ToDictionary(
                g => g.Key,
                g => new DesktopBaselineStats
                {
                    Days = days,
                    AverageActiveSeconds = ScaleToSpan(g.Sum(x => x.ActiveSeconds), days, spanDays),
                    AverageBackgroundSeconds = ScaleToSpan(g.Sum(x => x.BackgroundSeconds), days, spanDays)
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

internal class DesktopProcessTimeData
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }
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