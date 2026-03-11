using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class ActivityCategory : BaseNameTextColorIconEntity
{
    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();

    public virtual ICollection<TrackerDesktopMappingByPattern> TrackerDesktopMappingByPatternList { get; set; } = new List<TrackerDesktopMappingByPattern>();

    // public string icon { get; set; }
}