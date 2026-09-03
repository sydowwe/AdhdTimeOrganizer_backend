using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

public record UpdateTrackerAndroidMappingRequest
    : IUpdateRequest<TrackerAndroidMappingByPattern>, IMappingTargetRequest
{
    public string? PackageName { get; init; }
    public PatternMatchType? PackageNameMatchType { get; init; }

    public string? AppLabel { get; init; }
    public PatternMatchType? AppLabelMatchType { get; init; }

    public required bool IsActive { get; init; }

    // See UpdateTrackerDesktopMappingRequest — the same four members, for the same reason (CQ-10).
    public bool? IsIgnored { get; init; }
    public long? ActivityId { get; init; }
    public long? RoleId { get; init; }
    public long? CategoryId { get; init; }

    /// <remarks>
    /// A full replace, exactly as the desktop twin's <c>UpdateEntity</c>: a target member left out of the
    /// body clears the column rather than preserving what the row held, which is why the "exactly one
    /// target" rule now runs on update too.
    /// </remarks>
    public void UpdateEntity(TrackerAndroidMappingByPattern e)
    {
        e.PackageName = PackageName;
        e.PackageNameMatchType = PackageNameMatchType;
        e.AppLabel = AppLabel;
        e.AppLabelMatchType = AppLabelMatchType;
        e.IsActive = IsActive;
        e.IsIgnored = IsIgnored;
        e.ActivityId = ActivityId;
        e.RoleId = RoleId;
        e.CategoryId = CategoryId;
    }
}
