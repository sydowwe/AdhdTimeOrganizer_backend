using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record MoveToListTodoListItemRequest : IPatchRequest
{
    public required long DestinationListId { get; init; }
}