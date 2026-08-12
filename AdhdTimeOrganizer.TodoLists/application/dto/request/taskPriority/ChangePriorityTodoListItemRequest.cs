using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record ChangePriorityTodoListItemRequest : IPatchRequest
{
    public required long PriorityId { get; init; }
}