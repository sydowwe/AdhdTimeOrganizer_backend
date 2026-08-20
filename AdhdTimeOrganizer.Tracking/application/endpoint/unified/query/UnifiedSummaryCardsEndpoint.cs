using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.summaryCards;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.service.unified;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.application.validator.unified;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.unified.query;

/// <summary>
/// The merged top items, each against the user's own recent self.
///
/// <para>The baseline reads exactly as it does on the per-source cards — <b>the average day over the
/// lookback, multiplied back out to the length of the span</b> — because the two sit on one screen and
/// comparing a seven-day total against a one-day mean would report every ordinary week as up several
/// hundred percent.</para>
///
/// <para>The lookback is merged the same way the span is, with the same sources and the same
/// time-of-day window. Anything else would compare a de-overlapped figure against a double-counted one
/// and report the merge itself as a drop in usage.</para>
/// </summary>
public class UnifiedSummaryCardsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<UnifiedSummaryCardsRequest, List<UnifiedSummaryCardDto>>
{
    /// <summary>What the unified page asks for when it does not say.</summary>
    private const int DefaultTopN = 4;

    public override void Configure()
    {
        Post("/activity-tracking/unified/summary-cards");
        Validator<UnifiedSummaryCardsValidator>();
        Summary(s =>
        {
            s.Summary = "Get merged top-item summary cards with a baseline comparison";
            s.Description =
                "The top items across the selected trackers after the overlap rule, each compared " +
                "against the average day of the chosen lookback scaled to the length of the span.";
            s.Response<List<UnifiedSummaryCardDto>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(UnifiedSummaryCardsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var timeZone = await timeZones.GetAsync(userId, ct);
        var windows = req.ToDailyWindows(timeZone);
        var selected = req.SelectedSources();

        var span = await UnifiedSpan.BuildAsync(db, userId, windows, selected, ct);
        var baseline = await BuildBaselineAsync(userId, req, selected, windows, timeZone, ct);

        var response = span.Ledger.Entries
            .GroupBy(entry => entry.Label, StringComparer.Ordinal)
            .Select(g => new
            {
                Label = g.Key,
                Active = g.Sum(e => e.ActiveSeconds),
                Background = g.Sum(e => e.BackgroundSeconds),
                Sources = UnifiedLedger.SourceNamesOf(g)
            })
            .OrderByDescending(item => item.Active + item.Background)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .Take(req.TopN ?? DefaultTopN)
            .Select(item =>
            {
                var hasBaseline = baseline.TryGetValue(item.Label, out var reference);

                return new UnifiedSummaryCardDto
                {
                    Label = item.Label,
                    IsNew = !hasBaseline,
                    TotalSeconds = item.Active + item.Background,
                    // Null renders as no activity rather than as a zero -- and background is always
                    // null for an item only android saw, because that ledger records none.
                    Active = item.Active > 0
                        ? Stat(item.Active, hasBaseline ? reference.Active : null)
                        : null,
                    Background = item.Background > 0
                        ? Stat(item.Background, hasBaseline ? reference.Background : null)
                        : null,
                    Sources = item.Sources
                };
            })
            .ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private static ActivityStatDto Stat(int seconds, int? average) => new()
    {
        Seconds = seconds,
        AverageSeconds = average,
        PercentChange = average is null or 0 ? null : PercentChange(seconds, average.Value)
    };

    /// <summary>
    /// The lookback, merged with the same sources and the same per-day window as the span, reduced to
    /// the mean day and multiplied back out to the span's length.
    ///
    /// <para>An empty dictionary is a valid answer and the common one for a new user: every card comes
    /// back <c>isNew</c> and the client renders no comparison.</para>
    /// </summary>
    private async Task<Dictionary<string, (int Active, int Background)>> BuildBaselineAsync(
        long userId,
        UnifiedSummaryCardsRequest req,
        IReadOnlySet<TrackingSource> selected,
        DailyWindowSet spanWindows,
        TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        var empty = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

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
            BaselineType.AllTime => (
                await UnifiedActivityLoader.FirstActivityDayAsync(db, userId, selected, timeZone, ct) ?? earliest,
                -1, null),
            _ => (req.DateFrom.AddDays(-7), 7, null)
        };

        if (lookbackStart < earliest)
            lookbackStart = earliest;

        if (lookbackEnd < lookbackStart)
            return empty;

        var lookbackWindows = DailyWindowSet.Resolve(
            lookbackStart, lookbackEnd, req.From.ToTimeOnly(), req.To.ToTimeOnly(), timeZone);

        var lookback = await UnifiedSpan.BuildAsync(db, userId, lookbackWindows, selected, ct);

        var minutes = lookback.Merge.Minutes.AsEnumerable();

        // The weekday is a question about the user's calendar, not UTC's: read off the stored instant, a
        // Tuesday 00:30 session in Bratislava counts as Monday and lands in the wrong baseline.
        if (weekday.HasValue)
            minutes = minutes.Where(m => WallClockZone.FromUtc(m.Minute, timeZone).DayOfWeek == weekday.Value);

        var byLabel = minutes
            .GroupBy(m => m.Label, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (Active: g.Sum(m => m.ActiveSeconds), Background: g.Sum(m => m.BackgroundSeconds)),
                StringComparer.Ordinal);

        if (byLabel.Count == 0)
            return empty;

        if (nominalDays <= 0)
        {
            // AllTime: the span the history actually covers, first tracked day to last, inclusive.
            var first = DateOnly.FromDateTime(WallClockZone.FromUtc(lookback.Merge.Minutes.Min(m => m.Minute), timeZone));
            var last = DateOnly.FromDateTime(WallClockZone.FromUtc(lookback.Merge.Minutes.Max(m => m.Minute), timeZone));
            nominalDays = Math.Max(1, last.DayNumber - first.DayNumber + 1);
        }

        return byLabel.ToDictionary(
            kv => kv.Key,
            kv => (
                ScaleToSpan(kv.Value.Active, nominalDays, spanWindows.DayCount),
                ScaleToSpan(kv.Value.Background, nominalDays, spanWindows.DayCount)),
            StringComparer.Ordinal);
    }

    /// <summary>The mean day of the lookback, multiplied out to the length of the requested span.</summary>
    private static int ScaleToSpan(double lookbackSeconds, int lookbackDays, int spanDays) =>
        (int)Math.Round(lookbackSeconds / lookbackDays * spanDays, MidpointRounding.AwayFromZero);

    private static double PercentChange(int current, int average) =>
        Math.Round((double)(current - average) / average * 100, 1);
}
