using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.response.taskPlanner;

public record PlannerTaskResponse : BasePlannerTaskResponse
{
    public required PlannerTaskStatus Status { get; init; }
    public TimeDto? ActualStartTime { get; init; }
    public TimeDto? ActualEndTime { get; init; }

    public long? SourceTemplateTaskId { get; init; }

    public string? SkipReason { get; init; }

    public required long CalendarId { get; init; }
    public long? TodolistItemId { get; init; }
    // public required CalendarResponse Calendar { get; init; }
    // public TodoListResponse? Todolist { get; init; }

    public required string Color { get; init; }
    public required bool IsDone { get; init; }
}