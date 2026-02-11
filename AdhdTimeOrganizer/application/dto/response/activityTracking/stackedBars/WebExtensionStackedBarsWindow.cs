namespace AdhdTimeOrganizer.application.dto.response.activityTracking.stackedBars;

public record WebExtensionStackedBarsWindow
{
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public List<WebExtensionStackedBarsEntry> Activities { get; set; } = new();
}