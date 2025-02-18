using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;
using AdhdTimeOrganizer.Common.application.dto.request.@base;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record TaskUrgencyRequest : TextColorRequest
{
    [Required]
    public short Priority { get; init; }
}