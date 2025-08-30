using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record TimePeriodRequest : TextColorRequest
{
    [Required]
    public int Length { get; init; }

    public bool IsHiddenInView { get; init; } = false;
}