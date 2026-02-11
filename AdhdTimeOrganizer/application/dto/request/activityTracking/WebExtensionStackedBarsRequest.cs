using AdhdTimeOrganizer.application.dto.dto;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record WebExtensionStackedBarsRequest : DateAndTimeRangeDto
{
    public required int WindowMinutes { get; init; }  // Re-aggregate: 10, 15, 30, 60

    public int? MinSeconds { get; init; }  // Filter out domains below threshold
}