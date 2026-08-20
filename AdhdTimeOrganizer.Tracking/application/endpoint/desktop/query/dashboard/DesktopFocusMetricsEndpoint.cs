using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.helper;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.desktop.query.dashboard;

public class DesktopFocusMetricsEndpoint(DbContext db, IUserTimeZoneResolver timeZones)
    : BaseFocusMetricsEndpoint<FocusMetricsRequest>(timeZones)
{
    public override void Configure() =>
        ConfigureFocusMetrics<FocusMetricsValidator>("/activity-tracking/desktop/focus-metrics", "desktop");

    /// <summary>
    /// The primary lane only — not the detail lane, which is window titles inside a process and would
    /// report moving between two files as task switching, and not the background lane.
    ///
    /// <para>Sessions are keyed on the process name, which is what the timeline's primary lane switches
    /// between, but labelled with the product name, which is what <c>summary-cards</c> shows. Keying on
    /// the product name instead would merge every helper process of one suite into a single item and
    /// lose the switches between them — and merge everything with a blank product name into one.</para>
    /// </summary>
    protected override async Task<IReadOnlyList<IReadOnlyList<FocusSession>>> LoadAsync(
        FocusMetricsRequest req, long userId, DailyWindowSet windows, CancellationToken ct)
    {
        var from = windows.EnvelopeFrom;
        var to = windows.EnvelopeTo;

        var rows = await db.Set<DesktopActivityEntry>()
            .Where(x => x.UserId == userId)
            .Where(x => x.WindowStart >= from && x.WindowStart < to)
            .OrderBy(x => x.WindowStart)
            .ThenBy(x => x.ProcessName)
            .ToListAsync(ct);

        return Bucketize(rows, windows, x => x.WindowStart)
            .Select(day => (IReadOnlyList<FocusSession>)TimelineSegmentBuilder
                .Build(day.Select(r =>
                    new ActivityMinute(r.WindowStart, r.ProcessName, r.ProductName, r.ActiveSeconds)))
                .Primary
                .Select(s => new FocusSession(
                    s.Key,
                    string.IsNullOrWhiteSpace(s.Label) ? s.Key : s.Label,
                    s.StartedAt,
                    s.EndedAt,
                    s.DurationSeconds))
                .ToList())
            .ToList();
    }

    protected override async Task<DateOnly?> FirstActivityDayAsync(
        FocusMetricsRequest req, long userId, TimeZoneInfo timeZone, CancellationToken ct)
    {
        var earliest = await db.Set<DesktopActivityEntry>()
            .Where(x => x.UserId == userId)
            .MinAsync(x => (DateTime?)x.WindowStart, ct);

        return earliest.HasValue
            ? DateOnly.FromDateTime(WallClockZone.FromUtc(earliest.Value, timeZone))
            : null;
    }
}
