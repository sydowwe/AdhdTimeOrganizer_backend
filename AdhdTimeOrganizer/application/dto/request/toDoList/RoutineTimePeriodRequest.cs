using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record RoutineTimePeriodRequest : TextColorRequest
{
    [Required]
    public int LengthInDays { get; init; }

    public bool IsHidden { get; init; } = false;
}