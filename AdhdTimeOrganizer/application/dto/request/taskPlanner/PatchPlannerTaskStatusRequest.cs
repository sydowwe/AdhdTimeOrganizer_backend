using AdhdTimeOrganizer.Core.domain.model.@enum;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record PatchPlannerTaskStatusRequest
{
    public required PlannerTaskStatus Status { get; init; }
    public TimeDto? ActualStartTime { get; init; }
    public TimeDto? ActualEndTime { get; init; }
}