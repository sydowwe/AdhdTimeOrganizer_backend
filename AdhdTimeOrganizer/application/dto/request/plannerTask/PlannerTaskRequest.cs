using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.plannerTask;

public record PlannerTaskRequest : WithIsDoneRequest
{
    [Required]
    public required TimeOnly StartTime { get; init; }

    [Required]
    public required TimeOnly EndTime { get; init; }

    public bool IsBackground { get; init; }
    public bool IsOptional { get; init; }

    public string? Location { get; init; }
    public string? Notes { get; init; }

    [Required]
    public required int CalendarId { get; init; }

    public long? PriorityId { get; init; }
    public long? TodolistId { get; init; }
}