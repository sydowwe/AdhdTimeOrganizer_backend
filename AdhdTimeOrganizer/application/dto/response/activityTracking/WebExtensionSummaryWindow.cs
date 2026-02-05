namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public record WebExtensionSummaryWindow
{
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public List<WebExtensionSummaryEntry> Activities { get; set; } = new();
}