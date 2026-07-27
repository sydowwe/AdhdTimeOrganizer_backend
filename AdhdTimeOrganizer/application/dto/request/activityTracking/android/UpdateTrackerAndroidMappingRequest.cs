using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.android;

public record UpdateTrackerAndroidMappingRequest : IUpdateRequest<TrackerAndroidMappingByPattern>
{
    public string? PackageName { get; init; }
    public PatternMatchType? PackageNameMatchType { get; init; }

    public string? AppLabel { get; init; }
    public PatternMatchType? AppLabelMatchType { get; init; }

    public required bool IsActive { get; init; }

    public void UpdateEntity(TrackerAndroidMappingByPattern e)
    {
        e.PackageName = PackageName;
        e.PackageNameMatchType = PackageNameMatchType;
        e.AppLabel = AppLabel;
        e.AppLabelMatchType = AppLabelMatchType;
        e.IsActive = IsActive;
    }
}