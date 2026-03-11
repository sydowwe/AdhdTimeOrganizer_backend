using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record TrackerDesktopMappingFilter : IFilterRequest
{
    public required TrackerDesktopMappingTypeEnum Type { get; set; }
    public bool? IsActive { get; set; }

    public string? ProcessName { get; set; }
    public PatternMatchType? ProcessNameMatchType { get; set; }

    public string? ProductName { get; set; }
    public PatternMatchType? ProductNameMatchType { get; set; }

    public string? WindowTitle { get; set; }
    public PatternMatchType? WindowTitleMatchType { get; set; }

    public bool? IsIgnored { get; set; }
    public long? ActivityId { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
}
