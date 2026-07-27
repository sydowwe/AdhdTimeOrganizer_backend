using AdhdTimeOrganizer.domain.model.entity.activityTracking.android;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.android;

public record CreateTrackerAndroidMappingRequest : ICreateRequest<TrackerAndroidMappingByPattern>
{
    public string? PackageName { get; init; }
    public PatternMatchType? PackageNameMatchType { get; init; }

    public string? AppLabel { get; init; }
    public PatternMatchType? AppLabelMatchType { get; init; }

    public bool IsActive { get; init; } = true;

    public bool? IsIgnored { get; init; }
    public long? ActivityId { get; init; }
    public long? RoleId { get; init; }
    public long? CategoryId { get; init; }

    public TrackerAndroidMappingByPattern ToEntity => new()
    {
        UserId = 0,
        PackageName = PackageName,
        PackageNameMatchType = PackageNameMatchType,
        AppLabel = AppLabel,
        AppLabelMatchType = AppLabelMatchType,
        IsActive = IsActive,
        IsIgnored = IsIgnored,
        ActivityId = ActivityId,
        RoleId = RoleId,
        CategoryId = CategoryId
    };
}