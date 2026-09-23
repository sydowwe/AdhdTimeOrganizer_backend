using AdhdTimeOrganizer.Planning.domain.model.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner;

public record PatchPlannerTaskStatusRequest
{
    public required PlannerTaskStatus Status { get; init; }
    public TimeDto? ActualStartTime { get; init; }
    public TimeDto? ActualEndTime { get; init; }

    /// <summary>
    /// Why the task was skipped — free text, not constrained to <c>UserPlannerSettings.PredefinedSkipReasons</c>
    /// (the home widget offers its own reasons). Only accepted beside <c>Cancelled</c>; any other status with a
    /// non-blank reason is a 400. Trimmed, blank ⇒ null. This endpoint is the one writer of the column — the
    /// full PUT never sets it.
    /// </summary>
    public string? SkipReason { get; init; }
}