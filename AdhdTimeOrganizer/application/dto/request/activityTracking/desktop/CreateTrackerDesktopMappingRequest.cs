using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record CreateTrackerDesktopMappingRequest : ICreateRequest<TrackerDesktopMappingByPattern>
{
    public string? ProcessName { get; init; }
    public PatternMatchType? ProcessNameMatchType { get; init; }

    public string? ProductName { get; init; }
    public PatternMatchType? ProductNameMatchType { get; init; }

    public string? WindowTitle { get; init; }
    public PatternMatchType? WindowTitleMatchType { get; init; }

    public bool IsActive { get; init; } = true;

    // Exactly one target group must be set:
    // - IsIgnored = true
    // - ActivityId != null
    // - RoleId != null and/or CategoryId != null
    public bool? IsIgnored { get; init; }
    public long? ActivityId { get; init; }
    public long? RoleId { get; init; }
    public long? CategoryId { get; init; }

    public TrackerDesktopMappingByPattern ToEntity => new()
    {
        UserId = 0,
        ProcessName = ProcessName,
        ProcessNameMatchType = ProcessNameMatchType,
        ProductName = ProductName,
        ProductNameMatchType = ProductNameMatchType,
        WindowTitle = WindowTitle,
        WindowTitleMatchType = WindowTitleMatchType,
        IsActive = IsActive,
        IsIgnored = IsIgnored,
        ActivityId = ActivityId,
        RoleId = RoleId,
        CategoryId = CategoryId
    };
}