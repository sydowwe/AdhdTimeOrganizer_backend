using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record UpdateTodoListItemRequest : BaseUpdateTodoListRequest
{
    [Required]
    public long TaskPriorityId { get; init; }

    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }
}
