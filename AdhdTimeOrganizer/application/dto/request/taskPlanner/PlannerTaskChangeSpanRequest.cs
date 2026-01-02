using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner;

public record PlannerTaskChangeSpanRequest : IPatchRequest
{
    [Required]
    public required TimeDto StartTime { get; init; }

    [Required]
    public required TimeDto EndTime { get; init; }
}