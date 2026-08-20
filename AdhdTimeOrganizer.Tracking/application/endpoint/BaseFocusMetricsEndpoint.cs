using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.focusMetrics;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking;

/// <summary>
/// The fragmentation dashboard, shared by the three sources <b>and by the merged one</b>. Only
/// <see cref="LoadAsync"/> and <see cref="FirstActivityDayAsync"/> differ between them; everything
/// below — the span arithmetic, the baseline lookback, and the two-pointer bucketing — is the same on
/// all four, and the definitions in <see cref="FocusMetricsCalculator"/> are contract, so a second
/// transcription of any of it would be a second set of answers.
///
/// <para><b>Sessions are built one day-window at a time, never over the whole envelope.</b> That is
/// what keeps a block from spanning two days and the overnight interval out of the gap measure. It
/// also makes the single-day case identical to what the timeline endpoint returns, which is what the
/// contract means by "the same sessions".</para>
///
/// <para><typeparamref name="TRequest"/> is generic only because the merged route carries a
/// <c>sources</c> member the three per-source routes do not. The request is handed to
/// <see cref="LoadAsync"/> rather than stashed on the endpoint: FastEndpoints reuses instances, and a
/// field written in <c>HandleAsync</c> and read from a loader is the kind of state that works until two
/// requests arrive together.</para>
/// </summary>
public abstract class BaseFocusMetricsEndpoint<TRequest>(IUserTimeZoneResolver timeZones)
    : Endpoint<TRequest, FocusMetricsResponse>
    where TRequest : FocusMetricsRequest
{
    /// <summary>
    /// The far edge of an <c>allTime</c> lookback. <c>allTime</c> over a per-minute ledger with no
    /// bound is an unbounded read of a partitioned table, and unlike <c>summary-cards</c> — which sums
    /// rows in the database — this endpoint stitches every row into sessions in memory. A year of
    /// history is already more "typical self" than anyone reads, so the lookback stops there and the
    /// per-day divisor is derived from the data actually found.
    /// </summary>
    public const int MaxLookbackDays = DashboardDateRangeRules.MaxSpanDays;

    /// <summary>
    /// The primary-lane sessions of each window in <paramref name="windows"/>, aligned index for index
    /// with <see cref="DailyWindowSet.Windows"/> — an empty list for a day with no activity, never a
    /// short list.
    /// </summary>
    protected abstract Task<IReadOnlyList<IReadOnlyList<FocusSession>>> LoadAsync(
        TRequest req, long userId, DailyWindowSet windows, CancellationToken ct);

    /// <summary>
    /// The user's earliest tracked day on this source, on their own clock, or <c>null</c> when they
    /// have none. Only <see cref="BaselineType.AllTime"/> asks.
    /// </summary>
    protected abstract Task<DateOnly?> FirstActivityDayAsync(
        TRequest req, long userId, TimeZoneInfo timeZone, CancellationToken ct);

    protected void ConfigureFocusMetrics<TValidator>(string route, string source)
        where TValidator : IValidator
    {
        Post(route);
        Validator<TValidator>();
        Summary(s =>
        {
            s.Summary = $"Get {source} attention-fragmentation metrics, optionally against a baseline";
            s.Description =
                "Switch count, longest unbroken block, median session length and longest interior break " +
                "over the requested span, computed inside each day's time-of-day window. Optionally " +
                "compares against the user's own recent lookback.";
            s.Response<FocusMetricsResponse>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var timeZone = await timeZones.GetAsync(userId, ct);
        var windows = req.ToDailyWindows(timeZone);

        var span = FocusMetricsCalculator.Compute(
            windows.Windows, await LoadAsync(req, userId, windows, ct), req.FocusGapSeconds);

        var baseline = req.Baseline.HasValue
            ? await BuildBaselineAsync(userId, req, req.Baseline.Value, windows, timeZone, ct)
            : null;

        await Send.ResponseAsync(new FocusMetricsResponse
        {
            SessionCount = span.SessionCount,
            DaysWithActivity = span.DaysWithActivity,
            SwitchCount = span.SwitchCount,
            MedianSessionSeconds = span.MedianSessionSeconds,
            LongestGapSeconds = span.LongestGapSeconds,
            LongestBlock = span.LongestBlock == null
                ? null
                : new FocusBlockDto
                {
                    Label = span.LongestBlock.Label,
                    StartedAt = span.LongestBlock.StartedAt,
                    EndedAt = span.LongestBlock.EndedAt,
                    Seconds = span.LongestBlock.Seconds
                },
            Baseline = baseline
        }, cancellation: ct);
    }

    /// <summary>
    /// The lookback, measured with the same time-of-day window and the same gap tolerance as the span
    /// it is compared against — a baseline computed over whole days would compare a working-day figure
    /// against a round-the-clock one.
    ///
    /// <para>Returning <c>null</c> is a valid answer, and the common one for a new user: the client
    /// renders the metrics with no comparison and nothing breaks.</para>
    /// </summary>
    private async Task<FocusBaselineDto?> BuildBaselineAsync(
        long userId,
        TRequest req,
        BaselineType baselineType,
        DailyWindowSet spanWindows,
        TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        var lookbackEnd = req.DateFrom.AddDays(-1);
        var earliest = req.DateFrom.AddDays(-MaxLookbackDays);

        // Nominal length of the lookback, which is the divisor for the per-day mean. -1 means "derive
        // it from the data", the same escape AllTime takes on summary-cards.
        var (lookbackStart, nominalDays, weekday) = baselineType switch
        {
            BaselineType.Last7Days => (req.DateFrom.AddDays(-7), 7, (DayOfWeek?)null),
            BaselineType.Last30Days => (req.DateFrom.AddDays(-30), 30, null),
            // Eight weeks back holds exactly eight of any one weekday.
            BaselineType.SameWeekday => (req.DateFrom.AddDays(-56), 8, req.DateFrom.DayOfWeek),
            BaselineType.AllTime => (await FirstActivityDayAsync(req, userId, timeZone, ct) ?? earliest, -1, null),
            _ => (req.DateFrom.AddDays(-7), 7, null)
        };

        if (lookbackStart < earliest)
            lookbackStart = earliest;

        if (lookbackEnd < lookbackStart)
            return null;

        var lookbackWindows = DailyWindowSet.Resolve(
            lookbackStart, lookbackEnd, req.From.ToTimeOnly(), req.To.ToTimeOnly(), timeZone);

        var result = FocusMetricsCalculator.Compute(
            lookbackWindows.Windows, await LoadAsync(req, userId, lookbackWindows, ct), req.FocusGapSeconds);

        // SameWeekday reads the weekday off the user's own calendar, which the window set already
        // carries: a Tuesday 00:30 session in Bratislava belongs to Monday's window, not Tuesday's.
        var days = weekday.HasValue
            ? result.Days.Where(d => d.Day.DayOfWeek == weekday.Value).ToList()
            : result.Days;

        if (days.Sum(d => d.SessionCount) == 0)
            return null;

        var activeDays = days.Where(d => d.SessionCount > 0).ToList();

        if (nominalDays <= 0)
        {
            // AllTime: the span the history actually covers, first active day to last, inclusive.
            var first = activeDays[0].Day;
            var last = activeDays[^1].Day;
            nominalDays = last.DayNumber - first.DayNumber + 1;
        }

        var blocks = activeDays.Where(d => d.LongestBlock != null).Select(d => d.LongestBlock!.Seconds).ToList();
        var gaps = activeDays.Where(d => d.LongestGapSeconds.HasValue).Select(d => d.LongestGapSeconds!.Value).ToList();

        return new FocusBaselineDto
        {
            // A count scales with the length of the span, so the mean day is multiplied back out to it
            // — the same like-for-like reading summary-cards takes, and for the same reason: comparing
            // a five-day total against a one-day mean reports every ordinary week as up several hundred
            // percent. The divisor is the nominal lookback, so days away pull the typical day down,
            // which is honest for "how much do you usually switch".
            SwitchCount = Round(days.Sum(d => d.SwitchCount) / (double)nominalDays * spanWindows.DayCount),

            // Scale-free: the span's own figure is the median over every session in it, so the
            // comparable lookback figure is the median over every session in the lookback.
            MedianSessionSeconds = Round(FocusMetricsCalculator.Median(
                days.SelectMany(d => d.SessionSeconds).ToList())),

            // The mean of each day's own longest block -- not the longest block in the whole lookback,
            // which is a record to beat and exactly the scoreboard framing this dashboard avoids.
            // Averaged over active days only: a day the user was away has no longest block, and
            // counting it as zero would report a week off as a collapse in focus.
            LongestBlockSeconds = blocks.Count > 0 ? Round(blocks.Average()) : null,
            LongestGapSeconds = gaps.Count > 0 ? Round(gaps.Average()) : null
        };
    }

    private static double Round(double value) => Math.Round(value, 1);

    /// <summary>
    /// Splits chronologically ordered rows into one bucket per day window, dropping the rows the
    /// envelope read picked up from the nights in between. A two-pointer walk rather than a filter per
    /// day, because a year-long span is 366 passes over tens of thousands of one-minute rows.
    /// </summary>
    protected static List<List<T>> Bucketize<T>(
        IReadOnlyList<T> orderedRows, DailyWindowSet windows, Func<T, DateTime> instant)
    {
        var buckets = new List<List<T>>(windows.DayCount);
        for (var i = 0; i < windows.DayCount; i++)
            buckets.Add([]);

        var day = 0;

        foreach (var row in orderedRows)
        {
            var at = instant(row);

            while (day < windows.DayCount && at >= windows.Windows[day].To)
                day++;

            if (day >= windows.DayCount)
                break;

            // Between two windows -- one of the nights the daily window excludes.
            if (at >= windows.Windows[day].From)
                buckets[day].Add(row);
        }

        return buckets;
    }
}
