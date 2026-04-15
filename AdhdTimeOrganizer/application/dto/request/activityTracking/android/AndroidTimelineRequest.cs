using AdhdTimeOrganizer.application.dto.dto;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.android;

public record AndroidTimelineRequest : DateAndTimeRangeDto
{
    public long? MinSeconds { get; init; }
}
