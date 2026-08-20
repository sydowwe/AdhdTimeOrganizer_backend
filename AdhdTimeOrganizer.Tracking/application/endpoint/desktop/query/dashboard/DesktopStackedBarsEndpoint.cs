using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopStackedBarsEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones) : Endpoint<WebExtensionStackedBarsRequest, IEnumerable<DesktopStackedBarsWindow>>
{
    public override void Configure()
    {
        Post("/activity-tracking/desktop/stacked-bars");
        Summary(s =>
        {
            s.Summary = "Get desktop process usage stacked bars data";
            s.Description = "Returns process usage breakdown grouped by time windows (active and background seconds) for a given date range";
            s.Response<IEnumerable<DesktopStackedBarsWindow>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<WebExtensionSummaryValidator>();
    }

    public override async Task HandleAsync(WebExtensionStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var timeZone = await timeZones.GetAsync(userId, ct);
        var windows = req.ToDailyWindows(timeZone);

        // One range predicate over the span's outer envelope, then the gaps between the daily windows
        // are dropped in memory.
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rawData = windows.Restrict(
            await dbContext.Set<DesktopActivityEntry>()
                .Where(x => x.UserId == userId)
                .Where(x => x.WindowStart >= from && x.WindowStart < to)
                .OrderBy(x => x.WindowStart)
                .ToListAsync(ct),
            x => x.WindowStart);

        var aggregated = AggregateIntoWindows(rawData, windows.Tile(req.WindowMinutes));

        if (req.MinSeconds is > 0)
            aggregated = FilterByMinSecondsWithOther(aggregated, req.MinSeconds.Value);

        var response = aggregated
            .OrderBy(w => w.WindowStart)
            .ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private static List<DesktopStackedBarsWindow> FilterByMinSecondsWithOther(
        List<DesktopStackedBarsWindow> windows,
        int minSeconds)
    {
        return windows
            .Select(w =>
            {
                var aboveThreshold = w.Activities
                    .Where(a => a.TotalSeconds >= minSeconds)
                    .ToList();

                var belowThreshold = w.Activities
                    .Where(a => a.TotalSeconds < minSeconds)
                    .ToList();

                if (belowThreshold.Count > 0)
                    aboveThreshold.Add(new DesktopStackedBarsEntry
                    {
                        ProcessName = "_other",
                        ProductName = "_other",
                        ActiveSeconds = belowThreshold.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = belowThreshold.Sum(x => x.BackgroundSeconds)
                    });

                return new DesktopStackedBarsWindow
                {
                    WindowStart = w.WindowStart,
                    WindowEnd = w.WindowEnd,
                    Activities = aboveThreshold
                        .OrderByDescending(a => a.TotalSeconds)
                        .ToList()
                };
            })
            .Where(w => w.Activities.Count > 0)
            .ToList();
    }

    /// <summary>
    /// Buckets the one-minute ledger rows into the bands <see cref="DailyWindowSet.Tile"/> laid out.
    /// The bands are the user's, not UTC's, and they are truncated at each day's window edge rather
    /// than spilling across it — see the tiling rule on <c>Tile</c>. Empty bands are omitted; the client
    /// generates and merges the empty slots itself, and over a span that is most of them.
    ///
    /// <para><c>windowStart</c> is unique across the response because the bands come from one
    /// chronological tiling rather than from a minute-of-day alignment, which over a span would collide
    /// between days.</para>
    /// </summary>
    private static List<DesktopStackedBarsWindow> AggregateIntoWindows(
        List<DesktopActivityEntry> rawData,
        IReadOnlyList<AggregationWindow> buckets)
    {
        return rawData
            .GroupBy(x => DailyWindowSet.BucketIndexOf(buckets, x.WindowStart))
            .Where(windowGroup => windowGroup.Key >= 0)
            .Select(windowGroup => new DesktopStackedBarsWindow
            {
                WindowStart = buckets[windowGroup.Key].Start,
                WindowEnd = buckets[windowGroup.Key].End,
                Activities = windowGroup
                    .GroupBy(x => x.ProcessName)
                    .Select(processGroup => new DesktopStackedBarsEntry
                    {
                        ProcessName = processGroup.Key,
                        ProductName = processGroup
                            .Where(x => !string.IsNullOrEmpty(x.ProductName))
                            .Select(x => x.ProductName)
                            .FirstOrDefault() ?? processGroup.Key,
                        ActiveSeconds = processGroup.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = processGroup.Sum(x => x.BackgroundSeconds)
                    })
                    .OrderByDescending(x => x.TotalSeconds)
                    .ToList()
            })
            .ToList();
    }
}