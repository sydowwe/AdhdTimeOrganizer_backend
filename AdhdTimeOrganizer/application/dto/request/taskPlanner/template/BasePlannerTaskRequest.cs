using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner.template;

public record BasePlannerTaskRequest
{
    [Required]
    public required TimeOnly StartTime { get; init; }

    [Required]
    public required TimeOnly EndTime { get; init; }

    [Required]
    public required bool IsBackground { get; init; }

    [Required]
    public required bool IsOptional { get; init; }

    [StringLength(200)]
    public string? Location { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }

    [Required]
    public required long ActivityId { get; init; }

    public long? PriorityId { get; init; }
}