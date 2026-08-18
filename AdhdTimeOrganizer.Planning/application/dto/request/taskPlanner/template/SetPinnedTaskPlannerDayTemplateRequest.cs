using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;

/// <summary>
/// Sets (not toggles) the pinned state of one template. Absolute rather than a toggle so two devices
/// clicking "pin" on the same template converge instead of flipping each other back and forth.
/// </summary>
public record SetPinnedTaskPlannerDayTemplateRequest : IPatchRequest
{
    public required bool IsPinned { get; init; }
}
