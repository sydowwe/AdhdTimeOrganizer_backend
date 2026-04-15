namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidAppPieData
{
    public required string PackageName { get; init; }
    public required string AppLabel { get; init; }
    public required long Seconds { get; init; }
    public required long TotalSeconds { get; init; }
}
