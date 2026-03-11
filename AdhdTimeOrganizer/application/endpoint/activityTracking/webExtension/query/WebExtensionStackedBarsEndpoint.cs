using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.stackedBars;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionStackedBarsEndpoint(AppDbContext dbContext) : Endpoint<WebExtensionStackedBarsRequest, IEnumerable<WebExtensionStackedBarsWindow>>
{
    public override void Configure()
    {
        Post("/activity-tracking/web-extension/stacked-bars");
        Validator<WebExtensionSummaryValidator>();
    }

    public override async Task HandleAsync(WebExtensionStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        // 1. Fetch raw 1-min window data from DB
        var rawData = await dbContext.WebExtensionActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ToListAsync(ct);

        var windowMinutes = req.WindowMinutes;

        // 3. Re-aggregate into target window size
        var aggregated = AggregateIntoWindows(rawData, windowMinutes);

        // 4. Apply minimum seconds filter
        if (req.MinSeconds is > 0)
        {
            aggregated = FilterByMinSecondsWithOther(aggregated, req.MinSeconds.Value);
        }

        // 5. Build response
        var response = aggregated
            .OrderBy(w => w.WindowStart)
            .ToList();

        await SendAsync(response, cancellation: ct);
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
                {
                    aboveThreshold.Add(new WebExtensionStackedBarsEntry
                    {
                        Domain = "_other",
                        ActiveSeconds = belowThreshold.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = belowThreshold.Sum(x => x.BackgroundSeconds)
                    });
                }

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

    private static List<WebExtensionStackedBarsWindow> AggregateIntoWindows(
        List<WebExtensionActivityEntry> rawData,
        int targetWindowMinutes)
    {
        // Group raw records into target window buckets
        var grouped = rawData
            .GroupBy(x => AlignToWindow(x.WindowStart, targetWindowMinutes))
            .Select(windowGroup => new WebExtensionStackedBarsWindow
            {
                WindowStart = windowGroup.Key,
                WindowEnd = windowGroup.Key.AddMinutes(targetWindowMinutes),
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

    private static DateTime AlignToWindow(DateTime time, int windowMinutes)
    {
        // Round down to nearest window boundary
        // e.g., 09:07 with 15-min windows -> 09:00
        // e.g., 09:18 with 15-min windows -> 09:15
        var totalMinutes = (int)(time.TimeOfDay.TotalMinutes);
        var alignedMinutes = (totalMinutes / windowMinutes) * windowMinutes;
        return time.Date.AddMinutes(alignedMinutes);
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