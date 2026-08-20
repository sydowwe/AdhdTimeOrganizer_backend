namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;

public record PieChartRequest : DateRangeAndTimeRangeDto
{
    public double? MinPercent { get; init; } // Minimum percentage threshold (e.g., 1.0 for 1%)
}