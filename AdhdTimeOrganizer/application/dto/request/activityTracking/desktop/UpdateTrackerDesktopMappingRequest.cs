using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record UpdateTrackerDesktopMappingRequest : IUpdateRequest
{
    public string? ProcessName { get; init; }
    public PatternMatchType? ProcessNameMatchType { get; init; }

    public string? ProductName { get; init; }
    public PatternMatchType? ProductNameMatchType { get; init; }

    public string? WindowTitle { get; init; }
    public PatternMatchType? WindowTitleMatchType { get; init; }

    public required bool IsActive { get; init; }
}
