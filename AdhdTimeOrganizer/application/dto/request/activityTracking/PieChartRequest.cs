using AdhdTimeOrganizer.application.dto.dto;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record PieChartRequest : DateAndTimeRangeDto
{
    public double? MinPercent { get; init; } // Minimum percentage threshold (e.g., 1.0 for 1%)
}