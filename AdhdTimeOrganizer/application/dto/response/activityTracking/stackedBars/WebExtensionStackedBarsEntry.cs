namespace AdhdTimeOrganizer.application.dto.response.activityTracking.stackedBars;

public class WebExtensionStackedBarsEntry
{
    public string Domain { get; set; } = string.Empty;
    public string? Url { get; set; }  // Most visited URL in this window
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}