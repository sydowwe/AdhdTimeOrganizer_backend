using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

public record AndroidStackedBarsRequest : DateAndTimeRangeDto
{
    public required int WindowMinutes { get; init; }

    public long? MinSeconds { get; init; }
}