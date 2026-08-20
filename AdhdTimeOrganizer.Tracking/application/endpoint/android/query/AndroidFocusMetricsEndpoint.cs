using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.android.query;

public class AndroidFocusMetricsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : BaseFocusMetricsEndpoint<FocusMetricsRequest>(timeZones)
{
    public override void Configure() =>
        ConfigureFocusMetrics<FocusMetricsValidator>("/activity-tracking/android/focus-metrics", "Android app");

    /// <summary>
    /// Android has one lane: the ledger already stores real foreground sessions, so there is nothing to
    /// stitch out of per-minute rows and <c>sessions</c> is the primary lane the other two sources call
    /// <c>primarySessions</c>.
    ///
    /// <para>A session is filed under the day its <b>start</b> falls in, matching the timeline, and its
    /// end is clipped to that day's window. Clipping matters here and nowhere else: the other two
    /// sources are built from one-minute rows that cannot reach past the window edge, while a phone
    /// session left open across the evening boundary would otherwise contribute a block running through
    /// hours the user explicitly excluded.</para>
    /// </summary>
    protected override async Task<IReadOnlyList<IReadOnlyList<FocusSession>>> LoadAsync(
        FocusMetricsRequest req, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<AndroidSessionData>()
            .Where(x => x.UserId == userId)
            .Where(x => x.SessionStartUtc >= from && x.SessionStartUtc < to)
            .OrderBy(x => x.SessionStartUtc)
            .ThenBy(x => x.PackageName)
            .ToListAsync(ct);

        var buckets = Bucketize(rows, windows, x => x.SessionStartUtc);

        return buckets
            .Select((day, i) => (IReadOnlyList<FocusSession>)day
                .Select(s =>
                {
                    var endedAt = s.SessionEndUtc > windows.Windows[i].To ? windows.Windows[i].To : s.SessionEndUtc;
                    if (endedAt < s.SessionStartUtc)
                        endedAt = s.SessionStartUtc;

                    return new FocusSession(
                        s.PackageName, s.AppLabel, s.SessionStartUtc, endedAt,
                        (endedAt - s.SessionStartUtc).TotalSeconds);
                })
                .ToList())
            .ToList();
    }

    protected override async Task<DateOnly?> FirstActivityDayAsync(
        FocusMetricsRequest req, long userId, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var earliest = await db.Set<AndroidSessionData>()
            .Where(x => x.UserId == userId)
            .MinAsync(x => (DateTime?)x.SessionStartUtc, ct);

        return earliest.HasValue
            ? DateOnly.FromDateTime(WallClockZone.FromUtc(earliest.Value, timeZone))
            : null;
    }
}
