using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.dto.request.activityTracking.android;

public record AndroidDistinctEntriesFilter : IFilterRequest
{
    public string? PackageName { get; set; }
    public PatternMatchType? PackageNameMatchType { get; set; }

    public string? AppLabel { get; set; }
    public PatternMatchType? AppLabelMatchType { get; set; }
}