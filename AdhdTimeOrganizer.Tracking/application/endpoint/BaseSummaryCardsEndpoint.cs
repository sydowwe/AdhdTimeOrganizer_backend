using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using FastEndpoints;
using FluentValidation;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking;

/// <summary>
/// One ledger row, reduced to the four things the cards actually read. The per-source endpoints project
/// into this after their own read so that everything past the read — the grouping, the baseline
/// lookback, the weekday fold, the per-day divisor and the scaling — happens exactly once for all three
/// sources rather than three times with three sets of drift.
///
/// <para><paramref name="Detail"/> is the secondary label a source carries beside its grouping key
/// (desktop's product name, android's package name) and is <c>null</c> where there is none. The first
/// non-empty one in a group wins.</para>
///
/// <para>Seconds are <c>long</c> because android's are; the sources whose columns are <c>int</c> narrow
/// again on the way into their own DTOs.</para>
/// </summary>
public readonly record struct SummaryRow(
    string Key,
    string? Detail,
    DateTime Instant,
    long ActiveSeconds,
    long BackgroundSeconds);

/// <summary>One key's total over the requested span.</summary>
public sealed record SummaryItem(string Key, string? Detail, long ActiveSeconds, long BackgroundSeconds)
{
    public long TotalSeconds => ActiveSeconds + BackgroundSeconds;
}

/// <summary>
/// One key's figures over the lookback, already reduced to the mean day and multiplied back out to the
/// length of the requested span — so it is directly comparable against the <see cref="SummaryItem"/>
/// beside it. <paramref name="Days"/> is the nominal length of the lookback that divided it.
/// </summary>
public sealed record SummaryBaseline(int Days, long ActiveSeconds, long BackgroundSeconds);

/// <summary>
/// The top-items-against-your-own-recent-self dashboard, shared by the three per-source cards.
///
/// <para>Only <see cref="LoadAsync"/> and <see cref="BuildItem"/> differ between them. Everything else —
/// the lookback arithmetic, the per-day window applied to <b>both</b> the span and the lookback, the
/// weekday fold on the user's own calendar, the all-time floor and the scaling — is the same on all
/// three, and it is the same reading <see cref="BaseFocusMetricsEndpoint{TRequest}"/> and the merged
/// card already take. The two sit on one screen, so a second transcription of it is a second answer to
/// "compared to last 7 days".</para>
///
/// <para><b>The baseline is read through the daily windows too.</b> An unrestricted lookback over the
/// raw envelope counts every night the user's time-of-day window excludes — on a 09:00–17:00 window
/// that is a baseline roughly three times too large, i.e. every ordinary week reported as a collapse.
/// </para>
///
/// <para><b><see cref="BaselineType.AllTime"/> is floored at
/// <see cref="DashboardDateRangeRules.MaxSpanDays"/> back</b>, not at <c>DateTime.MinValue</c>. The
/// floor is a bounded read of a partitioned ledger rather than one from the beginning of time, and it
/// is a <c>DateOnly</c> resolved through the user's zone rather than an <c>Unspecified</c>-kind
/// <c>DateTime</c>, which Npgsql refuses to write to a <c>timestamptz</c>. The divisor is still derived
/// from the data actually found, so a user with three days of history is still divided by three.</para>
///
/// <para><typeparamref name="TRequest"/> is generic so a route can carry members the others do not (the
/// merged card's <c>sources</c> is the shape). The request is handed to <see cref="LoadAsync"/> rather
/// than stashed on the endpoint: FastEndpoints reuses instances, and a field written in
/// <c>HandleAsync</c> and read from a loader is the kind of state that works until two requests arrive
/// together.</para>
/// </summary>
public abstract class BaseSummaryCardsEndpoint<TRequest, TItem>(IUserTimeZoneResolver timeZones)
    : Endpoint<TRequest, List<TItem>>
    where TRequest : SummaryCardsRequest
{
    /// <summary>
    /// How many cards a source returns when the client does not say. Per-source because the pages ask
    /// for different numbers of them.
    /// </summary>
    protected abstract int DefaultTopN { get; }

    /// <summary>
    /// This source's rows inside <paramref name="windows"/> — read over the window set's outer envelope
    /// and then put through <see cref="DailyWindowSet.Restrict{T}"/>, because asking the database for
    /// each day separately would be N round trips over a partitioned ledger and asking it for the
    /// envelope alone would count every night the window excludes.
    ///
    /// <para>Called twice per request, once for the span and once for the lookback, with different
    /// window sets. It must not read anything off the request except the source's own members.</para>
    /// </summary>
    protected abstract Task<IReadOnlyList<SummaryRow>> LoadAsync(
        TRequest req, long userId, DailyWindowSet windows, CancellationToken ct);

    /// <summary>
    /// This source's card for one item. <paramref name="baseline"/> is <c>null</c> when the lookback
    /// never saw the key, which is what the client renders as <c>isNew</c>.
    /// </summary>
    protected abstract TItem BuildItem(SummaryItem current, SummaryBaseline? baseline);

    protected void ConfigureSummaryCards<TValidator>(string route, string summary, string description)
        where TValidator : IValidator
    {
        Post(route);
        Validator<TValidator>();
        Summary(s =>
        {
            s.Summary = summary;
            s.Description = description;
            s.Response<List<TItem>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var timeZone = await timeZones.GetAsync(userId, ct);
        var windows = req.ToDailyWindows(timeZone);

        var current = await LoadAsync(req, userId, windows, ct);
        var baseline = await BuildBaselineAsync(req, userId, windows, timeZone, ct);

        var response = Aggregate(current)
            .OrderByDescending(item => item.TotalSeconds)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Take(req.TopN ?? DefaultTopN)
            .Select(item => BuildItem(item, baseline.GetValueOrDefault(item.Key)))
            .ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }

    /// <summary>
    /// The lookback, measured through the same per-day window as the span it is compared against and
    /// reduced to the mean day, then multiplied back out to the span's length: the card's own number is
    /// a span total, and comparing a seven-day total against a one-day mean reports every ordinary week
    /// as up 600%. On a single-day request the multiplier is 1 and the arithmetic is the unchanged one.
    ///
    /// <para>An empty dictionary is a valid answer and the common one for a new user: every card comes
    /// back <c>isNew</c> and the client renders no comparison.</para>
    /// </summary>
    private async Task<Dictionary<string, SummaryBaseline>> BuildBaselineAsync(
        TRequest req, long userId, DailyWindowSet spanWindows, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var empty = new Dictionary<string, SummaryBaseline>(StringComparer.Ordinal);

        var lookbackEnd = req.DateFrom.AddDays(-1);
        var earliest = req.DateFrom.AddDays(-DashboardDateRangeRules.MaxSpanDays);

        // Nominal length of the lookback, which is the divisor for the mean day. -1 means "derive it
        // from the data", the escape AllTime takes.
        var (lookbackStart, nominalDays, weekday) = req.Baseline switch
        {
            BaselineType.Last7Days => (req.DateFrom.AddDays(-7), 7, (DayOfWeek?)null),
            BaselineType.Last30Days => (req.DateFrom.AddDays(-30), 30, null),
            // Eight weeks back holds exactly eight of any one weekday.
            BaselineType.SameWeekday => (req.DateFrom.AddDays(-56), 8, req.DateFrom.DayOfWeek),
            BaselineType.AllTime => (earliest, -1, null),
            _ => (req.DateFrom.AddDays(-7), 7, null)
        };

        if (lookbackStart < earliest)
            lookbackStart = earliest;

        if (lookbackEnd < lookbackStart)
            return empty;

        var lookbackWindows = DailyWindowSet.Resolve(
            lookbackStart, lookbackEnd, req.From.ToTimeOnly(), req.To.ToTimeOnly(), timeZone);

        var rows = await LoadAsync(req, userId, lookbackWindows, ct);

        // The weekday is a question about the user's calendar, not UTC's: read straight off the stored
        // instant, a Tuesday 00:30 session in Bratislava counts as Monday and lands in the wrong
        // baseline.
        if (weekday.HasValue)
            rows = rows.Where(r => WallClockZone.FromUtc(r.Instant, timeZone).DayOfWeek == weekday.Value).ToList();

        if (rows.Count == 0)
            return empty;

        if (nominalDays <= 0)
        {
            // AllTime: the span the history actually covers, first tracked day to last, inclusive — and
            // likewise on the user's calendar, because this is the divisor of every average on the cards.
            var first = DateOnly.FromDateTime(WallClockZone.FromUtc(rows.Min(r => r.Instant), timeZone));
            var last = DateOnly.FromDateTime(WallClockZone.FromUtc(rows.Max(r => r.Instant), timeZone));
            nominalDays = last.DayNumber - first.DayNumber + 1;
        }

        if (nominalDays <= 0)
            nominalDays = 1;

        var days = nominalDays;

        return Aggregate(rows).ToDictionary(
            item => item.Key,
            item => new SummaryBaseline(
                days,
                ScaleToSpan(item.ActiveSeconds, days, spanWindows.DayCount),
                ScaleToSpan(item.BackgroundSeconds, days, spanWindows.DayCount)),
            StringComparer.Ordinal);
    }

    private static IEnumerable<SummaryItem> Aggregate(IReadOnlyList<SummaryRow> rows) =>
        rows.GroupBy(r => r.Key, StringComparer.Ordinal)
            .Select(g => new SummaryItem(
                g.Key,
                g.Select(r => r.Detail).FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                g.Sum(r => r.ActiveSeconds),
                g.Sum(r => r.BackgroundSeconds)));

    /// <summary>
    /// The mean day of the baseline, multiplied out to the length of the requested span. Multiplied
    /// before the divide so a short-but-busy baseline is not rounded away.
    /// </summary>
    private static long ScaleToSpan(long baselineTotalSeconds, int baselineDays, int spanDays) =>
        baselineTotalSeconds * spanDays / baselineDays;

    /// <summary>
    /// The card's change against its baseline, as a percentage. A baseline of zero has no ratio, so the
    /// answer is the presence of activity rather than a division.
    /// </summary>
    protected static double PercentChange(long current, long average)
    {
        if (average == 0)
            return current > 0 ? 100.0 : 0.0;

        return Math.Round((double)(current - average) / average * 100, 1);
    }
}
