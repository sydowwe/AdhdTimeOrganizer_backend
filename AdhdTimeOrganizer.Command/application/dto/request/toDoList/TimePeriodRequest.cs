using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record TimePeriodRequest : TextColorRequest
{
    [Required]
    public int Length { get; init; }

    public bool IsHiddenInView { get; init; } = false;
}