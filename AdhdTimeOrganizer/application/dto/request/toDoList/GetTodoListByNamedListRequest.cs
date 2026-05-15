using System.ComponentModel.DataAnnotations;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record GetTodoListItemsByListRequest
{
    [Required]
    [BindFrom("TodoListId")]
    public long TodoListId { get; init; }
}
