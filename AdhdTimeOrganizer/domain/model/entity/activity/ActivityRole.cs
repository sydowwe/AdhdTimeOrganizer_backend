using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class ActivityRole : BaseEntityWithUser, IBaseNameTextColorIconEntity
{
    public required string Name { get; set; }
    public string? Text { get; set; }
    public required string Color { get; set; }
    public string? Icon { get; set; }

    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();

    public virtual ICollection<TrackerDesktopMappingByPattern> TrackerDesktopMappingByPatternList { get; set; } = new List<TrackerDesktopMappingByPattern>();
    public virtual ICollection<TrackerAndroidMappingByPattern> TrackerAndroidMappingByPatternList { get; set; } = new List<TrackerAndroidMappingByPattern>();
}