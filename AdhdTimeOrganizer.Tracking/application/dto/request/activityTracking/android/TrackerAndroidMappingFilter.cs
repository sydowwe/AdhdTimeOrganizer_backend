using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

public record TrackerAndroidMappingFilter : IFilterRequest
{
    public required TrackerDesktopMappingTypeEnum Type { get; set; }
    public bool? IsActive { get; set; }

    public string? PackageName { get; set; }
    public PatternMatchType? PackageNameMatchType { get; set; }

    public string? AppLabel { get; set; }
    public PatternMatchType? AppLabelMatchType { get; set; }

    public bool? IsIgnored { get; set; }
    public long? ActivityId { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
}