namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

/// <inheritdoc cref="BaseTimelineRequest"/>
public record AndroidTimelineRequest : DateRangeAndTimeRangeDto
{
    public long? MinSeconds { get; init; }
}
