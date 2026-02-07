using AdhdTimeOrganizer.application.dto.request;
using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityHistory.webExtension.query;

public class WebExtensionSummaryEndpoint(AppDbContext dbContext) : Endpoint<WebExtensionSummaryRequest, WebExtensionSummaryResponse>
{
    public override void Configure()
    {
        Get("/activity-tracking/web-extension/summary");
        Policies("ActivityTracking"); // Allow extension clients with ActivityTracking role
        Validator<WebExtensionSummaryValidator>();
    }

    public override async Task HandleAsync(WebExtensionSummaryRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        // 1. Fetch raw 5-min window data from DB
        var rawData = await dbContext.WebExtensionData
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= req.From && x.WindowStart < req.To)
            .OrderBy(x => x.WindowStart)
            .ToListAsync(ct);

        // 2. Determine target window size (default 5 min)
        var windowMinutes = req.WindowMinutes ?? 5;

        // 3. Re-aggregate into target window size
        var aggregated = AggregateIntoWindows(rawData, windowMinutes);

        // 4. Apply minimum seconds filter
        if (req.MinSeconds.HasValue && req.MinSeconds > 0)
        {
            aggregated = FilterByMinSecondsWithOther(aggregated, req.MinSeconds.Value);
        }

        // 5. Build response
        var response = new WebExtensionSummaryResponse
        {
            Windows = aggregated
                .OrderBy(w => w.WindowStart)
                .ToList()
        };

        await SendAsync(response, cancellation: ct);
    }

    private List<WebExtensionSummaryWindow> FilterByMinSecondsWithOther(
        List<WebExtensionSummaryWindow> windows,
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
                    aboveThreshold.Add(new WebExtensionSummaryEntry
                    {
                        Domain = "_other",
                        ActiveSeconds = belowThreshold.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = belowThreshold.Sum(x => x.BackgroundSeconds)
                    });
                }

                return new WebExtensionSummaryWindow
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

    private static List<WebExtensionSummaryWindow> AggregateIntoWindows(
        List<WebExtensionData> rawData,
        int targetWindowMinutes)
    {
        // Group raw records into target window buckets
        var grouped = rawData
            .GroupBy(x => AlignToWindow(x.WindowStart, targetWindowMinutes))
            .Select(windowGroup => new WebExtensionSummaryWindow
            {
                WindowStart = windowGroup.Key,
                WindowEnd = windowGroup.Key.AddMinutes(targetWindowMinutes),
                Activities = windowGroup
                    // Group by domain within the window
                    .GroupBy(x => x.Domain)
                    .Select(domainGroup => new WebExtensionSummaryEntry
                    {
                        Domain = domainGroup.Key,
                        // Sum up seconds from all 5-min windows that fall into this target window
                        ActiveSeconds = domainGroup.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = domainGroup.Sum(x => x.BackgroundSeconds)
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

    private static List<WebExtensionSummaryWindow> FilterByMinSeconds(
        List<WebExtensionSummaryWindow> windows,
        int minSeconds)
    {
        return windows
            .Select(w => new WebExtensionSummaryWindow
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