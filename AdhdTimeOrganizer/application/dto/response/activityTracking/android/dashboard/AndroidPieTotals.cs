namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidPieTotals
{
    public required long TotalSeconds { get; init; }
    public required int TotalApps { get; init; }
    public required int TotalSessions { get; init; }
}
