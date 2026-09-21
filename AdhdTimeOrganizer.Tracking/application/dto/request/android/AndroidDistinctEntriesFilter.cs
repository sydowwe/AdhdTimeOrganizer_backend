using AdhdTimeOrganizer.Tracking.application.dto.@enum;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;

public record AndroidDistinctEntriesFilter : IFilterRequest
{
    public string? PackageName { get; set; }
    public PatternMatchType? PackageNameMatchType { get; set; }

    public string? AppLabel { get; set; }
    public PatternMatchType? AppLabelMatchType { get; set; }

    /// <summary>Null is no opinion (<see cref="MappingCoverage.All"/>) — B24.</summary>
    public MappingCoverage? Coverage { get; set; }
}
