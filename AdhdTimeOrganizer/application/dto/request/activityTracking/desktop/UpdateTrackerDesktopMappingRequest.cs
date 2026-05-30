using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.domain.model.entity.activityTracking.desktop;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;

public record UpdateTrackerDesktopMappingRequest : IUpdateRequest<TrackerDesktopMappingByPattern>
{
    public string? ProcessName { get; init; }
    public PatternMatchType? ProcessNameMatchType { get; init; }

    public string? ProductName { get; init; }
    public PatternMatchType? ProductNameMatchType { get; init; }

    public string? WindowTitle { get; init; }
    public PatternMatchType? WindowTitleMatchType { get; init; }

    public required bool IsActive { get; init; }

    public void UpdateEntity(TrackerDesktopMappingByPattern e)
    {
        e.ProcessName = ProcessName;
        e.ProcessNameMatchType = ProcessNameMatchType;
        e.ProductName = ProductName;
        e.ProductNameMatchType = ProductNameMatchType;
        e.WindowTitle = WindowTitle;
        e.WindowTitleMatchType = WindowTitleMatchType;
        e.IsActive = IsActive;
    }
}
