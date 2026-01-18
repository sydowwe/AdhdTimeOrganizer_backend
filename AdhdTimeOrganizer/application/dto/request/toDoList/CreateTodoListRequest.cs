using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record CreateTodoListRequest : BaseCreateTodoListRequest
{
    [Required]
    public long TaskPriorityId { get; init; }
}