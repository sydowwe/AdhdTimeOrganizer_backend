using AdhdTimeOrganizer.application.dto.dto;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.android;

public record AndroidTimelineRequest : DateAndTimeRangeDto
{
    public long? MinSeconds { get; init; }
}