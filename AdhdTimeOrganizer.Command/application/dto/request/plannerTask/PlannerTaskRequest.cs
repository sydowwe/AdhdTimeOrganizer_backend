using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.plannerTask;

public record PlannerTaskRequest : WithIsDoneRequest
{
    [Required]
    public DateTime StartTimestamp { get; init; }

    [Range(1, 720)]
    public int MinuteLength { get; init; }
}