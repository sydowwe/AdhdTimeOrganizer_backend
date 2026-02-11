namespace AdhdTimeOrganizer.application.dto.response.activityTracking.timeline;

public record WebExtensionTimelineResponse
{
    public required List<TimelineSession> ActiveSessions { get; set; } = new();
    public required List<TimelineSession> BackgroundSessions { get; set; } = new();
}