using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

public record BaseTimelineRequest : DateAndTimeRangeDto
{
    public int? MinSeconds { get; set; } // Filter out sessions shorter than this
}