using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record ChangePriorityTodoListItemRequest : IPatchRequest
{
    [Required]
    public required long PriorityId { get; init; }
}
