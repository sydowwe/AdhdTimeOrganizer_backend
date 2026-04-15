namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android.dashboard;

public record AndroidTimelineResponse
{
    public required List<AndroidTimelineSession> Sessions { get; init; }
}
