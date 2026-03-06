using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;

public class ActivityTrackingSettingsDesktopEntryFormatting : BaseEntityWithUser
{
    public required bool IsSavedToMainHistory { get; set; }
    public required string ProcessKey { get; set; }
    public required string ProcessNice { get; set; }
    public string? TitleSplit { get; set; } // -;;;;0 split by '-' take first part

}