using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.plannerTask;

public record PlannerFilterRequest : IMyRequest
{
    [Required]
    public DateTime FilterDate { get; init; }

    [Range(1, 72)]
    public int HourSpan { get; init; }
}