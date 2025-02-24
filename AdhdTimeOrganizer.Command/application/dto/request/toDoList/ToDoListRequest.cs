using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record ToDoListRequest : WithIsDoneRequest
{
    [Required]
    public long TaskUrgencyId { get; init; }
}