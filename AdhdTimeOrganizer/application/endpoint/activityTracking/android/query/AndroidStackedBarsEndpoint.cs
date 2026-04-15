using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.endpoint.activityTracking.android.query;

public class AndroidStackedBarsEndpoint(AppDbContext db) : Endpoint<AndroidStackedBarsRequest, IEnumerable<AndroidStackedBarsWindow>>
{
    public override void Configure()
    {
        Post("/activity-tracking/android/stacked-bars");
        Validator<AndroidStackedBarsValidator>();
    }

    public override async Task HandleAsync(AndroidStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var (from, to) = req.ToDateTimeRange();

        var sessions = await db.AndroidSessionDataEntries
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc < to && x.SessionEndUtc > from)
            .ToListAsync(ct);

        var windows = BuildWindows(sessions, from, to, req.WindowMinutes);

        if (req.MinSeconds is > 0)
        {
            windows = FilterByMinSeconds(windows, req.MinSeconds.Value);
        }

        await SendAsync(windows.OrderBy(w => w.WindowStart), cancellation: ct);
    }

    private static List<AndroidStackedBarsWindow> BuildWindows(
        List<AndroidSessionData> sessions,
        DateTime from,
        DateTime to,
        int windowMinutes)
    {
        var windows = new List<AndroidStackedBarsWindow>();
        var windowStart = from;

        while (windowStart < to)
        {
            var windowEnd = windowStart.AddMinutes(windowMinutes);

            var overlapping = sessions.Where(s => s.SessionStartUtc < windowEnd && s.SessionEndUtc > windowStart);

            var apps = overlapping
                .GroupBy(s => (s.PackageName, s.AppLabel))
                .Select(g =>
                {
                    var seconds = g.Sum(s =>
                    {
                        var clampedStart = s.SessionStartUtc < windowStart ? windowStart : s.SessionStartUtc;
                        var clampedEnd = s.SessionEndUtc > windowEnd ? windowEnd : s.SessionEndUtc;
                        return (long)(clampedEnd - clampedStart).TotalSeconds;
                    });
                    return new AndroidWindowApp
                    {
                        PackageName = g.Key.PackageName,
                        AppLabel = g.Key.AppLabel,
                        Seconds = seconds
                    };
                })
                .Where(a => a.Seconds > 0)
                .OrderByDescending(a => a.Seconds)
                .ToList();

            if (apps.Count > 0)
            {
                windows.Add(new AndroidStackedBarsWindow
                {
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    Apps = apps
                });
            }

            windowStart = windowEnd;
        }

        return windows;
    }

    private static List<AndroidStackedBarsWindow> FilterByMinSeconds(
        List<AndroidStackedBarsWindow> windows,
        long minSeconds)
    {
        return windows
            .Select(w =>
            {
                var above = w.Apps.Where(a => a.Seconds >= minSeconds).ToList();
                var below = w.Apps.Where(a => a.Seconds < minSeconds).ToList();

                if (below.Count > 0)
                {
                    above.Add(new AndroidWindowApp
                    {
                        PackageName = "_other",
                        AppLabel = "_other",
                        Seconds = below.Sum(a => a.Seconds)
                    });
                }

                return new AndroidStackedBarsWindow
                {
                    WindowStart = w.WindowStart,
                    WindowEnd = w.WindowEnd,
                    Apps = above.OrderByDescending(a => a.Seconds).ToList()
                };
            })
            .Where(w => w.Apps.Count > 0)
            .ToList();
    }
}
