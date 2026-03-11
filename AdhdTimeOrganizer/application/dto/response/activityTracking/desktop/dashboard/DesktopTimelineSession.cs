namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopTimelineSession
{
    public required long Id { get; set; }
    public required string ProcessName { get; set; } = string.Empty;
    public required string ProductName { get; set; } = string.Empty;
    public required DateTime StartedAt { get; set; }
    public required DateTime EndedAt { get; set; }
    public required int DurationSeconds { get; set; }
    public required int TotalSeconds { get; set; }
}
