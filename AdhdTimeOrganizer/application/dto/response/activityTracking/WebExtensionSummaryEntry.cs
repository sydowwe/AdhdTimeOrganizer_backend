namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public class WebExtensionSummaryEntry
{
    public string Domain { get; set; } = string.Empty;
    public int ActiveSeconds { get; set; }
    public int BackgroundSeconds { get; set; }
    public int TotalSeconds => ActiveSeconds + BackgroundSeconds;
}