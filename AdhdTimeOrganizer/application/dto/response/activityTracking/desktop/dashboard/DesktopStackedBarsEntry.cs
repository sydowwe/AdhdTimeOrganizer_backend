namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public class DesktopStackedBarsEntry
{
    public string ProcessName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}
