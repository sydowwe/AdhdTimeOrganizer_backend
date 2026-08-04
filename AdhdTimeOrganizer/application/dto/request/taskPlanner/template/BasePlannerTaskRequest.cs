using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using Sydowwe.Framework.application.dto.dto;

namespace AdhdTimeOrganizer.application.dto.request.taskPlanner.template;

public record BasePlannerTaskRequest
{
    public required TimeDto StartTime { get; init; }


    public required TimeDto EndTime { get; init; }


    public required bool IsBackground { get; init; }

    [StringLength(200)]
    public string? Location { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }


    public required long ActivityId { get; init; }

    public long? ImportanceId { get; init; }
}