using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.plannerTask;

public record PlannerFilterRequest : IMyRequest
{
    [Required]
    public DateTime FilterDate { get; init; }

    [Range(1, 72)]
    public int HourSpan { get; init; }
}