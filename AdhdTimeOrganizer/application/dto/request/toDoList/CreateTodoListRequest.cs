using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record CreateTodoListItemRequest : BaseCreateTodoListRequest
{
    [Required]
    public long TaskPriorityId { get; init; }

    [Required]
    public long TodoListId { get; init; }
}
