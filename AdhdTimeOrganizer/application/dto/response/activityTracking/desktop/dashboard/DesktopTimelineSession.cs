namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopTimelineSession
{
    public required long Id { get; set; }
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }
    public required DateTime StartedAt { get; set; }
    public required DateTime EndedAt { get; set; }
    public required int DurationSeconds { get; set; }
    public required int TotalSeconds { get; set; }
}
