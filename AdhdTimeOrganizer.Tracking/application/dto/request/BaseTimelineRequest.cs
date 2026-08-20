namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

/// <summary>
/// The timeline is a single-day dashboard: <c>DateTo</c> must equal <c>DateFrom</c> and the validator
/// rejects anything else. A session timeline over a span is not a legible object — the axis alone runs
/// to thousands of ticks, and every untracked night draws as tracked-and-idle — so the client falls
/// back to stacked bars over a range. The span members are here for uniformity with the other three
/// dashboards, not because a range is supported.
/// </summary>
public record BaseTimelineRequest : DateRangeAndTimeRangeDto
{
    public int? MinSeconds { get; set; } // Filter out sessions shorter than this
}