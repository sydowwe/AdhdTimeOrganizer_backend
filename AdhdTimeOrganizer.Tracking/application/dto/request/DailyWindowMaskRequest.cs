using AdhdTimeOrganizer.Tracking.domain.helper;
using FastEndpoints;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

/// <summary>
/// The base the two details endpoints bind. They take an instant <see cref="From"/>/<see cref="To"/>
/// envelope rather than a day span, because their caller opens them from a dashboard selection and
/// hands them its outer bounds.
///
/// <para>Over a multi-day span that envelope is <b>wider than the dashboard it came from</b>: it
/// includes the nights the per-day time-of-day window excludes. The two optional members below carry
/// that window across, as minutes past midnight on the user's clock, so the panel can be restricted to
/// the same time the slice was measured over. Both or neither — with neither, the envelope is used as
/// given, which is what a single-day full-day selection means anyway.</para>
/// </summary>
public record DailyWindowMaskRequest
{
    private DateTime _from;
    private DateTime _to;

    /// <summary>
    /// Start of the envelope, as a <b>UTC instant</b> — normalized on binding, see
    /// <see cref="AsUtcInstant"/>.
    /// </summary>
    [QueryParam]
    public DateTime From
    {
        get => _from;
        set => _from = AsUtcInstant(value);
    }

    /// <summary>Exclusive end of the envelope, normalized the same way.</summary>
    [QueryParam]
    public DateTime To
    {
        get => _to;
        set => _to = AsUtcInstant(value);
    }

    /// <summary>Start of the per-day window, minutes past midnight on the user's clock (0–1439).</summary>
    [QueryParam]
    public int? WindowStartMinutes { get; set; }

    /// <summary>
    /// End of the per-day window, minutes past midnight on the user's clock (0–1439). At or before
    /// <see cref="WindowStartMinutes"/> it is a window over midnight, so equal values are the full 24
    /// hours of each day rather than an empty one.
    /// </summary>
    [QueryParam]
    public int? WindowEndMinutes { get; set; }

    public bool HasDailyWindow => WindowStartMinutes.HasValue && WindowEndMinutes.HasValue;

    /// <summary>
    /// A bound query-string value as the UTC instant it names. Both members above go through this on
    /// binding, so no consumer can observe one that is not <see cref="DateTimeKind.Utc"/>.
    ///
    /// <para><b>This is a correctness fix, not tidiness.</b> Both members are compared directly against
    /// <c>WindowStart</c>, a <c>timestamptz</c> column, and Npgsql refuses a <c>Local</c>- or
    /// <c>Unspecified</c>-kind parameter for that type outright. A query string carries whatever the
    /// client wrote: <c>…Z</c> binds as <c>Utc</c>, an offset like <c>+02:00</c> binds as <c>Local</c>,
    /// and a bare <c>2026-03-09T10:00:00</c> binds as <c>Unspecified</c> — so two of the three shapes a
    /// client may legitimately send used to throw, and the endpoint answered 500. No round trip caught
    /// it because the test harness enables <c>Npgsql.EnableLegacyTimestampBehavior</c> and the host does
    /// not; that divergence is <c>INFRA-1</c> in <c>review/history/01-testing.md</c>.</para>
    ///
    /// <para><c>Unspecified</c> is read as UTC rather than as the user's wall clock. These two are an
    /// <i>instant</i> envelope — the caller hands back bounds a dashboard gave it, and
    /// <see cref="DailyWindowSet.FromEnvelope"/> consumes them through
    /// <see cref="WallClockZone.FromUtc"/> — so UTC is the reading the contract already had. That also
    /// matches how every other client-supplied instant in this solution is closed
    /// (<c>ScheduleSpec.RunAtUtc</c>, <c>AndroidSyncEndpoint</c>). A value meant as a user's wall clock
    /// would need the zone, which this DTO does not have and the time-of-day members carry instead.</para>
    /// </summary>
    private static DateTime AsUtcInstant(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// The per-day windows the envelope stands for, or <c>null</c> when the caller sent no window and
    /// the envelope is to be taken as given.
    /// </summary>
    public DailyWindowSet? ToDailyWindows(TimeZoneInfo timeZone) =>
        HasDailyWindow
            ? DailyWindowSet.FromEnvelope(
                From,
                To,
                new TimeOnly(WindowStartMinutes!.Value / 60, WindowStartMinutes.Value % 60),
                new TimeOnly(WindowEndMinutes!.Value / 60, WindowEndMinutes.Value % 60),
                timeZone)
            : null;
}
