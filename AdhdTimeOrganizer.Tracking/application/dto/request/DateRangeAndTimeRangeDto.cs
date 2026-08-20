using AdhdTimeOrganizer.Tracking.domain.helper;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

/// <summary>
/// The base every tracking dashboard request binds: an inclusive span of calendar days plus the
/// time-of-day window to apply <b>to each day in it</b>.
///
/// <para>This is the range counterpart of the framework's <see cref="DateAndTimeRangeDto"/>, which the
/// History dashboards still use. <see cref="From"/> and <see cref="To"/> keep their meaning exactly —
/// zone-less wall clock, over-midnight when <c>To &lt;= From</c> — they are just no longer the ends of
/// the range. See <see cref="DailyWindowSet"/> for what that costs if read the other way.</para>
///
/// <para>A single day is <see cref="DateFrom"/> == <see cref="DateTo"/>, which resolves to the one
/// window the single-<c>date</c> requests produced. The legacy <c>date</c> member is gone: an unmapped
/// JSON member is ignored, so a client still sending it keeps working, and there is one code path
/// rather than two.</para>
/// </summary>
public record DateRangeAndTimeRangeDto
{
    public required DateOnly DateFrom { get; init; }
    public required DateOnly DateTo { get; init; }

    public required TimeDto From { get; init; }
    public required TimeDto To { get; init; }

    /// <summary>Calendar days in the span, inclusive of both ends — 1 for a single-day request.</summary>
    public int DayCount => DateTo.DayNumber - DateFrom.DayNumber + 1;

    /// <summary>
    /// The per-day windows this request names on the clocks of <paramref name="timeZone"/>, which must
    /// be the requesting user's own zone (<c>User.Timezone</c>) — never <see cref="TimeZoneInfo.Local"/>,
    /// which is the server's and means nothing to the caller.
    /// </summary>
    public DailyWindowSet ToDailyWindows(TimeZoneInfo timeZone) =>
        DailyWindowSet.Resolve(DateFrom, DateTo, From.ToTimeOnly(), To.ToTimeOnly(), timeZone);
}
