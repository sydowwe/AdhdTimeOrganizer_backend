using AdhdTimeOrganizer.application.dto.response.taskPlanner;
using TaskStatus = AdhdTimeOrganizer.domain.model.@enum.TaskStatus;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record PlannerTaskResponse : BasePlannerTaskResponse
{
    public required bool IsDone { get; init; }
    public required TaskStatus Status { get; init; }
    public TimeOnly? ActualStartTime { get; init; }
    public TimeOnly? ActualEndTime { get; init; }

    public required bool IsFromTemplate { get; init; }
    public long? SourceTemplateTaskId { get; init; }

    public string? SkipReason { get; init; }

    public required long CalendarId { get; init; }
    public long? TodolistId { get; init; }
    // public required CalendarResponse Calendar { get; init; }
    // public TodoListResponse? Todolist { get; init; }

    public required string Color { get; init; }
    public required int EstimatedMinuteLength { get; init; }
}