using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.unified;
using AdhdTimeOrganizer.Tracking.application.service.unified;
using AdhdTimeOrganizer.Tracking.application.validator.unified;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.unified.query;

/// <summary>
/// Where the day actually went, once the three trackers stop double-counting each other.
///
/// <para>Items are merged by <c>label</c> across sources, so an application both the desktop agent and
/// the phone saw is one slice in one colour rather than two.</para>
/// </summary>
public class UnifiedPieChartEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : Endpoint<UnifiedPieChartRequest, UnifiedPieChartResponse>
{
    /// <summary>The bucket the below-threshold items fold into, exactly as on the per-source pies.</summary>
    private const string OtherLabel = "Other";

    public override void Configure()
    {
        Post("/activity-tracking/unified/pie-chart");
        Validator<UnifiedPieChartValidator>();
        Summary(s =>
        {
            s.Summary = "Get the merged activity breakdown across the selected trackers";
            s.Description =
                "Aggregates the selected trackers into one breakdown by item, with overlapping " +
                "wall-clock time credited to a single source, optionally folding items below a " +
                "percentage threshold into 'Other'.";
            s.Response<UnifiedPieChartResponse>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(UnifiedPieChartRequest req, CancellationToken ct)
    {
        var userId = User.GetId();
        var windows = req.ToDailyWindows(await timeZones.GetAsync(userId, ct));
        var selected = req.SelectedSources();

        var span = await UnifiedSpan.BuildAsync(db, userId, windows, selected, ct);

        var items = span.Ledger.Entries
            .GroupBy(e => e.Label, StringComparer.Ordinal)
            .Select(g => new UnifiedPieItemDto
            {
                Label = g.Key,
                ActiveSeconds = g.Sum(e => e.ActiveSeconds),
                BackgroundSeconds = g.Sum(e => e.BackgroundSeconds),
                TotalSeconds = g.Sum(e => e.TotalSeconds),
                Entries = g.Sum(e => e.Entries),
                Sources = UnifiedLedger.SourceNamesOf(g)
            })
            .OrderByDescending(item => item.TotalSeconds)
            .ToList();

        // Read off the same rounded entries the items are, so the chips, the slices and the totals
        // cannot disagree about a second.
        var totals = new UnifiedPieTotalsDto
        {
            TotalSeconds = items.Sum(item => item.TotalSeconds),
            ActiveSeconds = items.Sum(item => item.ActiveSeconds),
            BackgroundSeconds = items.Sum(item => item.BackgroundSeconds),
            // Distinct over the whole span, and counted before the threshold folds anything away.
            TotalItems = items.Count,
            TotalSessions = CountSessions(span)
        };

        await Send.ResponseAsync(new UnifiedPieChartResponse
        {
            Items = ApplyMinPercent(items, totals.TotalSeconds, req.MinPercent),
            Totals = totals
        }, cancellation: ct);
    }

    /// <summary>
    /// Sessions as the <b>ledgers</b> record them — an android session as stored, a run of adjacent
    /// one-minute rows otherwise — counted once each if any part of them survived the merge.
    ///
    /// <para>Counting the merged runs instead would report a session that lost a minute in the middle
    /// as two, which says something about the overlap rule rather than about the user's day.</para>
    /// </summary>
    private static int CountSessions(UnifiedSpan span)
    {
        var surviving = span.Merge.Minutes
            .Select(m => (m.Source, m.Minute))
            .ToHashSet();

        return span.Loads
            .SelectMany(load => load.Runs)
            .Count(run => run.Minutes.Any(minute => surviving.Contains((run.Source, minute))));
    }

    private static List<UnifiedPieItemDto> ApplyMinPercent(
        List<UnifiedPieItemDto> items, int totalSeconds, double? minPercent)
    {
        if (!minPercent.HasValue || totalSeconds <= 0)
            return items;

        var threshold = totalSeconds * minPercent.Value / 100.0;

        var above = items.Where(item => item.TotalSeconds >= threshold).ToList();
        var below = items.Where(item => item.TotalSeconds < threshold).ToList();

        if (below.Count == 0)
            return above;

        // The folded seconds stay in the response rather than disappearing, so the items still sum to
        // the totals the page prints above them.
        above.Add(new UnifiedPieItemDto
        {
            Label = OtherLabel,
            ActiveSeconds = below.Sum(item => item.ActiveSeconds),
            BackgroundSeconds = below.Sum(item => item.BackgroundSeconds),
            TotalSeconds = below.Sum(item => item.TotalSeconds),
            Entries = below.Sum(item => item.Entries),
            Sources = below.SelectMany(item => item.Sources).Distinct().ToList()
        });

        return above;
    }
}
