namespace AdhdTimeOrganizer.application.dto.request.activityTracking.android;

public class AndroidSyncRequest
{
    public List<AndroidSessionItemDto> Sessions { get; set; } = new();
    public DateTime SyncedUpToUtc { get; set; }
    public string DeviceId { get; set; } = string.Empty;
}

public class AndroidSessionItemDto
{
    public string PackageName { get; set; } = string.Empty;
    public string AppLabel { get; set; } = string.Empty;
    public string SessionStartUtc { get; set; } = string.Empty;
    public string SessionEndUtc { get; set; } = string.Empty;
    public long DurationSeconds { get; set; }
}
