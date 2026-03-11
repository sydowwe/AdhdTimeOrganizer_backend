namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop.dashboard;

public record DesktopTimelineResponse
{
    public required List<DesktopTimelineSession> PrimarySessions { get; set; } = new();
    public required List<DesktopTimelineSession> DetailSessions { get; set; } = new();
    public required List<DesktopTimelineSession> BackgroundSessions { get; set; } = new();
}
