using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.@enum;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record PieChartRequest : DateAndTimeRangeDto
{
    public double? MinPercent { get; init; }  // Minimum percentage threshold (e.g., 1.0 for 1%)
}


