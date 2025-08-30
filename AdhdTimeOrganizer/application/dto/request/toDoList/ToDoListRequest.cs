using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record ToDoListRequest : WithIsDoneRequest
{
    [Required]
    public long TaskUrgencyId { get; init; }
}