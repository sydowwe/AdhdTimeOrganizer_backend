namespace AdhdTimeOrganizer.application.dto.response.activityTracking;

public record ActivityHeartbeatResponse
{
    public bool Success { get; init; }
    public int EventsProcessed { get; init; }
    public string? Message { get; init; }
}
