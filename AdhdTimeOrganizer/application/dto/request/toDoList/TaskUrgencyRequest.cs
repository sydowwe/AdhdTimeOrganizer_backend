using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record TaskPriorityRequest : TextColorRequest
{
    [Required]
    public short Priority { get; init; }
}