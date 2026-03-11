using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activityTracking;

public class AndroidSessionData
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public string DeviceId { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string AppLabel { get; set; } = string.Empty;
    public DateTime SessionStartUtc { get; set; }
    public DateTime SessionEndUtc { get; set; }
    public long DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
}
