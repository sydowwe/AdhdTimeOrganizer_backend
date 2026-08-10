using FastEndpoints;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record GetTodoListItemsByListRequest
{
    [BindFrom("TodoListId")]
    public long TodoListId { get; init; }
}