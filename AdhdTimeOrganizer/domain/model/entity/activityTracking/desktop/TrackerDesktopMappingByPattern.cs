using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;

public class TrackerDesktopMappingByPattern : BaseEntityWithUser
{
    public string? ProcessName { get; set; }
    public PatternMatchType? ProcessNameMatchType { get; set; }
    public string? ProductName { get; set; }
    public PatternMatchType? ProductNameMatchType { get; set; }

    public string? WindowTitle { get; set; }          // "*.github.com", "slack.exe", "code.exe"
    public PatternMatchType? WindowTitleMatchType { get; set; }

    public required bool IsActive { get; set; }
    public bool? IsIgnored { get; set; }

    public long? ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public long? RoleId { get; set; }
    public ActivityRole? Role { get; set; }
    public long? CategoryId { get; set; }
    public ActivityCategory? Category { get; set; }
}