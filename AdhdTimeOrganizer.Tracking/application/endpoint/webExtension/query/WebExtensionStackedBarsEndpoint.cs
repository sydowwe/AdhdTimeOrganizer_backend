using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.stackedBars;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionStackedBarsEndpoint(DbContext dbContext, IUserTimeZoneResolver timeZones) : Endpoint<WebExtensionStackedBarsRequest, IEnumerable<WebExtensionStackedBarsWindow>>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/stacked-bars");
        Validator<WebExtensionSummaryValidator>();
        Summary(s =>
        {
            s.Summary = "Get web activity aggregated into time windows for a stacked bar chart";
            s.Description = "Aggregates web extension activity into configurable time windows and returns by-domain breakdowns, optionally filtering small domains into an 'Other' category";
            s.Response<IEnumerable<WebExtensionStackedBarsWindow>>(200, "Success");
            s.Response(400, "Bad request");
        });
    }

    public override async Task HandleAsync(WebExtensionStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var timeZone = await timeZones.GetAsync(userId, ct);
        var (from, to) = req.ToDateTimeRange(timeZone);

        // 1. Fetch raw 1-min window data from DB
        var rawData = await dbContext.Set<WebExtensionActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ToListAsync(ct);

        var windowMinutes = req.WindowMinutes;

        // 3. Re-aggregate into target window size
        var aggregated = AggregateIntoWindows(rawData, windowMinutes, timeZone);

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
    /// Buckets the one-minute ledger rows into the requested band width. Bands are aligned to the
    /// <b>user's</b> clock: a 60-minute band runs 09:00–10:00 where the user is, not 09:00–10:00 UTC, which
    /// is what an alignment computed off the raw instant produced — bars an offset out of step with their
    /// own labels for everyone not on UTC, and half an hour out in the half-hour zones.
    /// <para>
    /// Both bounds are converted back through the zone rather than the end being the start plus the band
    /// width, so the band that contains a DST transition covers the wall clock it claims to.
    /// </para>
    /// </summary>
    private static List<WebExtensionStackedBarsWindow> AggregateIntoWindows(
        List<WebExtensionActivityEntry> rawData,
        int targetWindowMinutes,
        TimeZoneInfo timeZone)
    {
        // Group raw records into target window buckets
        var grouped = rawData
            .GroupBy(x => AlignToWindow(x.WindowStart, targetWindowMinutes, timeZone))
            .Select(windowGroup => new WebExtensionStackedBarsWindow
            {
                WindowStart = WallClockZone.ToUtc(windowGroup.Key, timeZone),
                WindowEnd = WallClockZone.ToUtc(windowGroup.Key.AddMinutes(targetWindowMinutes), timeZone),
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

    /// <summary>
    /// The band an instant falls in, as a zone-less wall clock on the user's calendar day — rounded down to
    /// the nearest boundary, so with 15-minute bands the user's 09:07 lands on 09:00 and their 09:18 on
    /// 09:15. Returned as a label rather than an instant because the caller needs it to derive the band's
    /// far edge too.
    /// </summary>
    private static DateTime AlignToWindow(DateTime instant, int windowMinutes, TimeZoneInfo timeZone)
    {
        var wallClock = WallClockZone.FromUtc(instant, timeZone);
        var alignedMinutes = (int)wallClock.TimeOfDay.TotalMinutes / windowMinutes * windowMinutes;
        return wallClock.Date.AddMinutes(alignedMinutes);
    }

    private static List<WebExtensionStackedBarsWindow> FilterByMinSeconds(
        List<WebExtensionStackedBarsWindow> windows,
        int minSeconds)
    {
        return windows
            .Select(w => new WebExtensionStackedBarsWindow
            {
                WindowStart = w.WindowStart,
                WindowEnd = w.WindowEnd,
                Activities = w.Activities
                    .Where(a => a.TotalSeconds >= minSeconds)
                    .ToList()
            })
            // Remove windows with no activities after filtering
            .Where(w => w.Activities.Count > 0)
            .ToList();
    }
}