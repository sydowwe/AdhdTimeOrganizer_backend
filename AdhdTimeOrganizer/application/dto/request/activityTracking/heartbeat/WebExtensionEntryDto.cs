namespace AdhdTimeOrganizer.application.dto.request.activityTracking.heartbeat;

public record WebExtensionEntryDto
{
    public required string Domain { get; init; }
    public required string Url { get; init; }
    public required int ActiveSeconds { get; init; }
    public required int BackgroundSeconds { get; init; }
}