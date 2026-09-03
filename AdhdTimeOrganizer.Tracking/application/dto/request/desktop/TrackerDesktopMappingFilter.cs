using AdhdTimeOrganizer.Tracking.domain.model.@enum;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;

public record TrackerDesktopMappingFilter : IFilterRequest
{
    /// <summary>
    /// The target shape to narrow to, or <c>null</c> for all of them. Optional as of CQ-15: it was
    /// <c>required</c>, which made "every mapping, filtered on something else" unrepresentable — a page
    /// that wanted only the inactive rules had to ask for one target shape at a time.
    /// </summary>
    public TrackerMappingTypeEnum? Type { get; set; }
    public bool? IsActive { get; set; }

    public string? ProcessName { get; set; }
    public PatternMatchType? ProcessNameMatchType { get; set; }

    public string? ProductName { get; set; }
    public PatternMatchType? ProductNameMatchType { get; set; }

    public string? WindowTitle { get; set; }
    public PatternMatchType? WindowTitleMatchType { get; set; }

    public bool? IsIgnored { get; set; }
    public long? ActivityId { get; set; }
    public long? RoleId { get; set; }
    public long? CategoryId { get; set; }
}