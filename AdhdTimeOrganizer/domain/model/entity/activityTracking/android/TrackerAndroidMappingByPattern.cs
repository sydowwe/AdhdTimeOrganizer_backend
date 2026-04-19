using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityTracking.android;

public class TrackerAndroidMappingByPattern : BaseEntityWithUser
{
    public string? PackageName { get; set; }
    public PatternMatchType? PackageNameMatchType { get; set; }

    public string? AppLabel { get; set; }
    public PatternMatchType? AppLabelMatchType { get; set; }

    public required bool IsActive { get; set; }
    public bool? IsIgnored { get; set; }

    public long? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public long? RoleId { get; set; }
    public ActivityRole? Role { get; set; }
    public long? CategoryId { get; set; }
    public ActivityCategory? Category { get; set; }
}
