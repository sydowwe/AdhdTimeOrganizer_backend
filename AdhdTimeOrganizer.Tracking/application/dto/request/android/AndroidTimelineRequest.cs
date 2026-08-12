using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

public record AndroidTimelineRequest : DateAndTimeRangeDto
{
    public long? MinSeconds { get; init; }
}