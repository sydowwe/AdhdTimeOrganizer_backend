using AdhdTimeOrganizer.application.dto.response.extendable;

namespace AdhdTimeOrganizer.application.dto.response.activityPlanning;

public record PlannerTaskResponse : WithIsDoneResponse
{
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }
    public required bool IsBackground { get; init; }
    public required bool IsOptional { get; init; }

    public required string Status { get; init; }
    public TimeOnly? ActualStartTime { get; init; }
    public TimeOnly? ActualEndTime { get; init; }

    public bool IsFromTemplate { get; init; }
    public long? SourceTemplateTaskId { get; init; }

    public string? Location { get; init; }
    public string? Notes { get; init; }
    public string? SkipReason { get; init; }

    public required int CalendarId { get; init; }
    public long? PriorityId { get; init; }
    public long? TodolistId { get; init; }

    public required string Color { get; init; }
    public required int EstimatedMinuteLength { get; init; }
}