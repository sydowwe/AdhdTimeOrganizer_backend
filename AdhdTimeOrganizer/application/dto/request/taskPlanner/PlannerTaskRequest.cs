using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record PlannerTaskRequest : BasePlannerTaskRequest, IMyRequest
{
    [Required]
    public required bool IsDone { get; init; }
    [Required]
    public required PlannerTaskStatus Status { get; init; }
    [Required]
    public required long CalendarId { get; init; }

    public long? TodolistId { get; init; }
}