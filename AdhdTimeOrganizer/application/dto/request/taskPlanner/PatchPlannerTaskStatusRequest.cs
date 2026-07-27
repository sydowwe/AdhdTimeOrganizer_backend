using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record PatchPlannerTaskStatusRequest
{
    public required PlannerTaskStatus Status { get; init; }
    public TimeDto? ActualStartTime { get; init; }
    public TimeDto? ActualEndTime { get; init; }
}