namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

public record AndroidSyncRequest
{
    public List<AndroidSessionItemDto> Sessions { get; set; } = new();
    public DateTime SyncedUpToUtc { get; set; }
    public string DeviceId { get; set; } = string.Empty;
}

public record AndroidSessionItemDto
{
    public string PackageName { get; set; } = string.Empty;
    public string AppLabel { get; set; } = string.Empty;
    public string SessionStartUtc { get; set; } = string.Empty;
    public string SessionEndUtc { get; set; } = string.Empty;
    public long DurationSeconds { get; set; }
}