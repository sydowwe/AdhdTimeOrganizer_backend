using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record UpdateTodoListRequest : BaseUpdateTodoListRequest
{
    [Required]
    public long TaskPriorityId { get; init; }
}