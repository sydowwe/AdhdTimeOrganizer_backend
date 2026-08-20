using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.response.activityTracking.android.dashboard;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FastEndpoints;
using Sydowwe.Framework.application.extensions;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

public class AndroidStackedBarsEndpoint(DbContext db, IUserTimeZoneResolver timeZones) : Endpoint<AndroidStackedBarsRequest, IEnumerable<AndroidStackedBarsWindow>>
{
    public override void Configure()
    {
        Post("/activity-tracking/android/stacked-bars");
        Summary(s =>
        {
            s.Summary = "Get Android app usage stacked bars data";
            s.Description = "Returns app usage breakdown grouped by time windows for a given date range";
            s.Response<IEnumerable<AndroidStackedBarsWindow>>(200, "Success");
            s.Response(400, "Bad request");
        });
        Validator<AndroidStackedBarsValidator>();
    }

    public override async Task HandleAsync(AndroidStackedBarsRequest req, CancellationToken ct)
    {
        var userId = User.GetId();

        var dailyWindows = req.ToDailyWindows(await timeZones.GetAsync(userId, ct));

        // Sessions are spans, not instants, so the read is an overlap test against the span's outer
        // envelope. The nights between the daily windows are excluded by the buckets rather than by the
        // read: a session that runs across one is clamped to the buckets it actually overlaps.
        var envelopeFrom = dailyWindows.EnvelopeFrom;
        var envelopeTo = dailyWindows.EnvelopeTo;

        var sessions = await db.Set<AndroidSessionData>()
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc < envelopeTo && x.SessionEndUtc > envelopeFrom)
            .ToListAsync(ct);

        var windows = BuildWindows(sessions, dailyWindows.Tile(req.WindowMinutes));

        if (req.MinSeconds is > 0)
            windows = FilterByMinSeconds(windows, req.MinSeconds.Value);

        await Send.ResponseAsync(windows.OrderBy(w => w.WindowStart), cancellation: ct);
    }

    /// <summary>
    /// Fills the bands <see cref="DailyWindowSet.Tile"/> laid out, clamping each session to the band it
    /// is being counted into. Empty bands are omitted; the client generates and merges the empty slots
    /// itself, and over a span that is most of them.
    /// </summary>
    private static List<AndroidStackedBarsWindow> BuildWindows(
        List<AndroidSessionData> sessions,
        IReadOnlyList<AggregationWindow> buckets)
    {
        var windows = new List<AndroidStackedBarsWindow>();

        foreach (var (windowStart, windowEnd) in buckets)
        {
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
                windows.Add(new AndroidStackedBarsWindow
                {
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    Apps = apps
                });
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
                    above.Add(new AndroidWindowApp
                    {
                        PackageName = "_other",
                        AppLabel = "_other",
                        Seconds = below.Sum(a => a.Seconds)
                    });

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