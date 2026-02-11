namespace AdhdTimeOrganizer.application.dto.request.activityTracking.heartbeat;

public record WebExtensionHeartbeatRequest
{
    public required DateTime HeartbeatAt { get; init; }
    public required DateTime WindowStart { get; init; }
    public required int WindowMinutes { get; init; }
    public required bool IsFinal { get; init; }
    public required List<WebExtensionEntryDto> Activities { get; init; }
}