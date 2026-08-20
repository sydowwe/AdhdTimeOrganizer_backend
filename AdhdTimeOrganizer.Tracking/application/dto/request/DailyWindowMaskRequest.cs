using AdhdTimeOrganizer.Tracking.domain.helper;
using FastEndpoints;

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
    [QueryParam]
    public DateTime From { get; set; }

    [QueryParam]
    public DateTime To { get; set; }

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
