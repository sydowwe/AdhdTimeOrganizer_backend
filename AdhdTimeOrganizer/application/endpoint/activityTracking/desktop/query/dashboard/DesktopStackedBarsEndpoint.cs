using AdhdTimeOrganizer.application.dto.request.activityTracking;
using AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopStackedBarsEndpoint(AppDbContext dbContext) : Endpoint<WebExtensionStackedBarsRequest, IEnumerable<DesktopStackedBarsWindow>>
{
    public override void Configure()
    {
        Post("/activity-tracking/desktop/stacked-bars");
        Validator<WebExtensionSummaryValidator>();
    }

    public override async Task HandleAsync(WebExtensionStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var rawData = await dbContext.DesktopActivityEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ToListAsync(ct);



        var windowMinutes = req.WindowMinutes;

        var aggregated = AggregateIntoWindows(rawData, windowMinutes);

        if (req.MinSeconds is > 0)
        {
            aggregated = FilterByMinSecondsWithOther(aggregated, req.MinSeconds.Value);
        }

        var response = aggregated
            .OrderBy(w => w.WindowStart)
            .ToList();

        await SendAsync(response, cancellation: ct);
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
                {
                    aboveThreshold.Add(new DesktopStackedBarsEntry
                    {
                        ProcessName = "_other",
                        ProductName = "_other",
                        ActiveSeconds = belowThreshold.Sum(x => x.ActiveSeconds),
                        BackgroundSeconds = belowThreshold.Sum(x => x.BackgroundSeconds)
                    });
                }

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

    private static List<DesktopStackedBarsWindow> AggregateIntoWindows(
        List<DesktopActivityEntry> rawData,
        int targetWindowMinutes)
    {
        return rawData
            .GroupBy(x => AlignToWindow(x.WindowStart, targetWindowMinutes))
            .Select(windowGroup => new DesktopStackedBarsWindow
            {
                WindowStart = windowGroup.Key,
                WindowEnd = windowGroup.Key.AddMinutes(targetWindowMinutes),
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

    private static DateTime AlignToWindow(DateTime time, int windowMinutes)
    {
        var totalMinutes = (int)time.TimeOfDay.TotalMinutes;
        var alignedMinutes = (totalMinutes / windowMinutes) * windowMinutes;
        return time.Date.AddMinutes(alignedMinutes);
    }
}
