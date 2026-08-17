using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.desktop.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.helper;

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
        var (from, to) = req.ToDateTimeRange(timeZone);

        var rawData = await dbContext.Set<DesktopActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ToListAsync(ct);


        var windowMinutes = req.WindowMinutes;

        var aggregated = AggregateIntoWindows(rawData, windowMinutes, timeZone);

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
    /// Buckets the one-minute ledger rows into the requested band width. Bands are aligned to the
    /// <b>user's</b> clock: a 60-minute band runs 09:00–10:00 where the user is, not 09:00–10:00 UTC, which
    /// is what an alignment computed off the raw instant produced — bars an offset out of step with their
    /// own labels for everyone not on UTC, and half an hour out in the half-hour zones.
    /// <para>
    /// Both bounds are converted back through the zone rather than the end being the start plus the band
    /// width, so the band that contains a DST transition covers the wall clock it claims to.
    /// </para>
    /// </summary>
    private static List<DesktopStackedBarsWindow> AggregateIntoWindows(
        List<DesktopActivityEntry> rawData,
        int targetWindowMinutes,
        TimeZoneInfo timeZone)
    {
        return rawData
            .GroupBy(x => AlignToWindow(x.WindowStart, targetWindowMinutes, timeZone))
            .Select(windowGroup => new DesktopStackedBarsWindow
            {
                WindowStart = WallClockZone.ToUtc(windowGroup.Key, timeZone),
                WindowEnd = WallClockZone.ToUtc(windowGroup.Key.AddMinutes(targetWindowMinutes), timeZone),
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

    /// <summary>
    /// The band an instant falls in, as a zone-less wall clock on the user's calendar day. Returned as a
    /// label rather than an instant because the caller needs it to derive the band's far edge too.
    /// </summary>
    private static DateTime AlignToWindow(DateTime instant, int windowMinutes, TimeZoneInfo timeZone)
    {
        var wallClock = WallClockZone.FromUtc(instant, timeZone);
        var alignedMinutes = (int)wallClock.TimeOfDay.TotalMinutes / windowMinutes * windowMinutes;
        return wallClock.Date.AddMinutes(alignedMinutes);
    }
}