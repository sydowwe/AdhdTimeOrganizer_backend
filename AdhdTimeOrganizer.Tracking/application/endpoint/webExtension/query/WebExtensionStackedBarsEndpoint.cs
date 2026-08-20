using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.stackedBars;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionStackedBarsEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones) : Endpoint<StackedBarsRequest, IEnumerable<WebExtensionStackedBarsWindow>>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/stacked-bars");
        Validator<StackedBarsValidator>();
        Summary(s =>
        {
            s.Summary = "Get web activity aggregated into time windows for a stacked bar chart";
            s.Description = "Aggregates web extension activity into configurable time windows and returns by-domain breakdowns, optionally filtering small domains into an 'Other' category";
            s.Response<IEnumerable<WebExtensionStackedBarsWindow>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(StackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var timeZone = await timeZones.GetAsync(userId, ct);
        var windows = req.ToDailyWindows(timeZone);

        // 1. Fetch raw 1-min window data from DB. One range predicate over the span's outer envelope,
        //    then the gaps between the daily windows are dropped in memory.
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rawData = windows.Restrict(
            await dbContext.Set<WebExtensionActivityEntry>()
                .Where(x => x.UserId == userId)
                .Where(x => x.WindowStart >= from && x.WindowStart < to)
                .OrderBy(x => x.WindowStart)
                .ToListAsync(ct),
            x => x.WindowStart);

        // 3. Re-aggregate into target window size
        var aggregated = AggregateIntoWindows(rawData, windows.Tile(req.WindowMinutes));

        // 4. Apply minimum seconds filter
        if (req.MinSeconds is > 0)
            aggregated = FilterByMinSecondsWithOther(aggregated, req.MinSeconds.Value);

        // 5. Build response
        var response = aggregated
            .OrderBy(w => w.WindowStart)
            .ToList();

        await Send.ResponseAsync(response, cancellation: ct);
    }

    private List<WebExtensionStackedBarsWindow> FilterByMinSecondsWithOther(
        List<WebExtensionStackedBarsWindow> windows,
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

                // Combine small activities into "Other" bucket
                if (belowThreshold.Count > 0)
                    aboveThreshold.Add(new WebExtensionStackedBarsEntry
                    {
                        Domain = "_other",
                        ActiveSeconds = belowThreshold.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = belowThreshold.Sum(x => x.BackgroundSeconds)
                    });

                return new WebExtensionStackedBarsWindow
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
    private static List<WebExtensionStackedBarsWindow> AggregateIntoWindows(
        List<WebExtensionActivityEntry> rawData,
        IReadOnlyList<AggregationWindow> buckets)
    {
        // Group raw records into target window buckets
        var grouped = rawData
            .GroupBy(x => DailyWindowSet.BucketIndexOf(buckets, x.WindowStart))
            .Where(windowGroup => windowGroup.Key >= 0)
            .Select(windowGroup => new WebExtensionStackedBarsWindow
            {
                WindowStart = buckets[windowGroup.Key].Start,
                WindowEnd = buckets[windowGroup.Key].End,
                Activities = windowGroup
                    // Group by domain within the window
                    .GroupBy(x => x.Domain)
                    .Select(domainGroup => new WebExtensionStackedBarsEntry
                    {
                        Domain = domainGroup.Key,
                        // Sum up seconds from all 1-min windows that fall into this target window
                        ActiveSeconds = domainGroup.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = domainGroup.Sum(x => x.BackgroundSeconds),
                        Url = domainGroup
                            .Where(x => !string.IsNullOrEmpty(x.Url))
                            .GroupBy(x => x.Url)
                            .OrderByDescending(g => g.Sum(x => x.ActiveSeconds + x.BackgroundSeconds))
                            .FirstOrDefault()?.Key
                    })
                    // Order by total time descending within each window
                    .OrderByDescending(x => x.TotalSeconds)
                    .ToList()
            })
            .ToList();

        return grouped;
    }

}