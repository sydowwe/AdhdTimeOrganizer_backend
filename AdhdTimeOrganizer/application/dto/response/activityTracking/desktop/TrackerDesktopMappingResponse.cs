using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.activityTracking.desktop;

public record TrackerDesktopMappingResponse : IIdResponse
{
    public long Id { get; init; }

    public string? ProcessName { get; set; }
    public PatternMatchType? ProcessNameMatchType { get; set; }

    public string? ProductName { get; set; }
    public PatternMatchType? ProductNameMatchType { get; set; }

    public string? WindowTitle { get; set; }
    public PatternMatchType? WindowTitleMatchType { get; set; }

    public bool IsActive { get; set; }

    public bool? IsIgnored { get; set; }
    public long? ActivityId { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
}
