using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record CreateTodoListRequest : BaseCreateTodoListRequest
{
    [Required]
    public long TaskUrgencyId { get; init; }
}