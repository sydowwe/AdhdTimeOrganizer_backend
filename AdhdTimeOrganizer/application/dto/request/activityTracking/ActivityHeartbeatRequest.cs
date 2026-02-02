namespace AdhdTimeOrganizer.application.dto.request.activityTracking;

public record ActivityHeartbeatRequest
{
    public DateTime HeartbeatAt { get; init; }
    public bool IsIdle { get; init; }
    public List<ActivityEventDto> Events { get; init; } = new();
}
