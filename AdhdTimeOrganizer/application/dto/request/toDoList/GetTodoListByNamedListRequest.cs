using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record GetTodoListItemsByListRequest
{
    [Required]
    public long TodoListId { get; init; }
}
