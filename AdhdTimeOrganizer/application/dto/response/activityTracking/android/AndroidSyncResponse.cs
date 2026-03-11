namespace AdhdTimeOrganizer.application.dto.response.activityTracking.android;

public class AndroidSyncResponse
{
    public int Accepted { get; set; }
    public int DuplicatesSkipped { get; set; }
    public string SyncedUpToUtc { get; set; } = string.Empty;
}
