using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record MoveToListTodoListItemRequest : IPatchRequest
{
    [Required]
    public required long DestinationListId { get; init; }
}
