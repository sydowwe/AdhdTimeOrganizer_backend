using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.webExtension.query;

public class WebExtensionFocusMetricsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : BaseFocusMetricsEndpoint<FocusMetricsRequest>(timeZones)
{
    public override void Configure() =>
        ConfigureFocusMetrics<FocusMetricsValidator>("/activity-tracking/web-extension/focus-metrics", "browsing");

    /// <summary>
    /// The primary lane only. Not the detail lane — that is a finer cut of the same attention (pages
    /// inside a domain) and counting it reports in-site navigation as task switching — and not the
    /// background lane, which by definition is what the user was not looking at.
    /// </summary>
    protected override async Task<IReadOnlyList<IReadOnlyList<FocusSession>>> LoadAsync(
        FocusMetricsRequest req, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<WebExtensionActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ThenBy(x => x.Domain)
            .ToListAsync(ct);

        return Bucketize(rows, windows, x => x.WindowStart)
            .Select(day => (IReadOnlyList<FocusSession>)TimelineSegmentBuilder
                .Build(day.Select(r => new ActivityMinute(r.WindowStart, r.Domain, r.Url, r.ActiveSeconds)))
                .Primary
                .Select(s => new FocusSession(
                    s.Key, s.Key, s.StartedAt, s.EndedAt, s.DurationSeconds))
                .ToList())
            .ToList();
    }

    protected override async Task<DateOnly?> FirstActivityDayAsync(
        FocusMetricsRequest req, long userId, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var earliest = await db.Set<WebExtensionActivityEntry>()
            .Where(x => x.UserId == userId)
            .MinAsync(x => (DateTime?)x.WindowStart, ct);

        return earliest.HasValue
            ? DateOnly.FromDateTime(WallClockZone.FromUtc(earliest.Value, timeZone))
            : null;
    }
}
