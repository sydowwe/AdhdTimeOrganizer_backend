using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.dto;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.filter;

public class CalendarFilter : IFilterRequest
{
    [Required]
    public required DateOnly From { get; init; }

    [Required]
    public required DateOnly Until { get; init; }
}