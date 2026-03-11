using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record CreateTrackerDesktopMappingRequest : ICreateRequest
{
    public string? ProcessName { get; init; }
    public PatternMatchType? ProcessNameMatchType { get; init; }

    public string? ProductName { get; init; }
    public PatternMatchType? ProductNameMatchType { get; init; }

    public string? WindowTitle { get; init; }
    public PatternMatchType? WindowTitleMatchType { get; init; }

    public required bool IsActive { get; init; }

    // Exactly one target group must be set:
    // - IsIgnored = true
    // - ActivityId != null
    // - RoleId != null and/or CategoryId != null
    public bool? IsIgnored { get; init; }
    public long? ActivityId { get; init; }
    public long? RoleId { get; init; }
    public long? CategoryId { get; init; }
}
