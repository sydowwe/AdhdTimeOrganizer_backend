namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidAppSummaryDto
{
    public required string PackageName { get; init; }
    public required string AppLabel { get; init; }
    public required bool IsNew { get; init; }
    public AndroidActivityStatDto? Stat { get; init; }
}
