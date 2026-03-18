namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;

public class DesktopStackedBarsEntry
{
    public required string ProcessName { get; set; }
    public string? ProductName { get; set; }
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}
