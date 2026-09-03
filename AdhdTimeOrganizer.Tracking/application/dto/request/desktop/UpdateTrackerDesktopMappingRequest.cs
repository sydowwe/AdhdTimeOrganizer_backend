using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking;
using Sydowwe.Framework.application.dto.request.@interface;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;

public record UpdateTrackerDesktopMappingRequest
    : IUpdateRequest<TrackerDesktopMappingByPattern>, IMappingTargetRequest
{
    public string? ProcessName { get; init; }
    public PatternMatchType? ProcessNameMatchType { get; init; }

    public string? ProductName { get; init; }
    public PatternMatchType? ProductNameMatchType { get; init; }

    public string? WindowTitle { get; init; }
    public PatternMatchType? WindowTitleMatchType { get; init; }

    public required bool IsActive { get; init; }

    // The same target group the create request carries, and written the same way (CQ-10). Until this
    // existed a rule could not be re-pointed from one activity to another at all — the user had to delete
    // and recreate it, which is the one thing TrackerMappingActivityReferenceSource's remark says is
    // expensive: lose a mapping and every future heartbeat matching that pattern silently stops being
    // attributed.
    public bool? IsIgnored { get; init; }
    public long? ActivityId { get; init; }
    public long? RoleId { get; init; }
    public long? CategoryId { get; init; }

    /// <remarks>
    /// A full replace, PUT's own semantic: a target member left out of the body clears the column rather
    /// than preserving what the row held. That is why the "exactly one target" rule now applies to update
    /// as well — without it, a body carrying no target at all would clear all four and die on
    /// <c>CK_TrackerDesktopMappingByPattern_TargetRequired</c> as a 500.
    /// </remarks>
    public void UpdateEntity(TrackerDesktopMappingByPattern e)
    {
        e.ProcessName = ProcessName;
        e.ProcessNameMatchType = ProcessNameMatchType;
        e.ProductName = ProductName;
        e.ProductNameMatchType = ProductNameMatchType;
        e.WindowTitle = WindowTitle;
        e.WindowTitleMatchType = WindowTitleMatchType;
        e.IsActive = IsActive;
        e.IsIgnored = IsIgnored;
        e.ActivityId = ActivityId;
        e.RoleId = RoleId;
        e.CategoryId = CategoryId;
    }
}
