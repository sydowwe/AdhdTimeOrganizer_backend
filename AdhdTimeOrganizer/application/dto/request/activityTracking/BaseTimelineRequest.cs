using AdhdTimeOrganizer.application.dto.dto;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record BaseTimelineRequest : DateAndTimeRangeDto
{
    public int? MinSeconds { get; set; }  // Filter out sessions shorter than this
}
