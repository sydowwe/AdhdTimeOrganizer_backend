using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record PlannerTaskRequest : BasePlannerTaskRequest, IMyRequest
{
    [Required]
    public required bool IsDone { get; init; }
    [Required]
    public required int CalendarId { get; init; }

    public long? TodolistId { get; init; }
}