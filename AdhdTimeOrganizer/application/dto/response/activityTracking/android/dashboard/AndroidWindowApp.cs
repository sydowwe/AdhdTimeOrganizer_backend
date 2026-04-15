namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidWindowApp
{
    public required string PackageName { get; init; }
    public required string AppLabel { get; init; }
    public required long Seconds { get; init; }
}
