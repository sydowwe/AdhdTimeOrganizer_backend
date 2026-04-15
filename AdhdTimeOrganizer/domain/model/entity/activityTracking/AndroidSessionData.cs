using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activityTracking;

public class AndroidSessionData : BaseEntityWithUser
{
    public string DeviceId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string AppLabel { get; set; } = string.Empty;
    public DateTime SessionStartUtc { get; set; }
    public DateTime SessionEndUtc { get; set; }
    public long DurationSeconds { get; set; }
}
