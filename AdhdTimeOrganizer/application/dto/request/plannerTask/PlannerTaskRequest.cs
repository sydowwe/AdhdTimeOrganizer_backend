using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.plannerTask;

public record PlannerTaskRequest : WithIsDoneRequest
{
    [Required]
    public required DateTime StartTimestamp { get; init; }
    [Required, Range(1, 720)]
    public required int MinuteLength { get; init; }
    [Required, StringLength(7)]
    public required string Color { get; init; }
}