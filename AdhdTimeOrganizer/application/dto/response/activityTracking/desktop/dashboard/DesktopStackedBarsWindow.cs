namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopStackedBarsWindow
{
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
    public List<DesktopStackedBarsEntry> Activities { get; set; } = new();
}
