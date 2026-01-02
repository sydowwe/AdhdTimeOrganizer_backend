using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class PlannerTaskFilter : IFilterRequest
{
    [Required]
    public required long CalendarId { get; init; }
    [Required]
    public required TimeDto From { get; init; }

    [Required]
    public required TimeDto Until { get; init; }
}