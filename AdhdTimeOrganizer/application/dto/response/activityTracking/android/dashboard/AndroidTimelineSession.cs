namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public class AndroidTimelineSession
{
    public long Id { get; set; }
    public required string PackageName { get; init; }
    public required string AppLabel { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime EndedAt { get; init; }
    public required long DurationSeconds { get; init; }
    public required long TotalSeconds { get; set; }
}
