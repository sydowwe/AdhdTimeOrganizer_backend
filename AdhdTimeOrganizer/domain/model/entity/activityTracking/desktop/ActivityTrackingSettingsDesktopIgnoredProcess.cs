using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;

public class ActivityTrackingSettingsDesktopIgnoredProcess : BaseEntityWithUser
{
    public required string ProcessKey { get; set; }
    public bool? TitleContainsToggle { get; set; }
    public string? TitleContains { get; set; }
}