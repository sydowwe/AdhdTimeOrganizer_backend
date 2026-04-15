namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidActivityStatDto
{
    public long Seconds { get; init; }
    public long? AverageSeconds { get; init; }
    public double? PercentChange { get; init; }
}
