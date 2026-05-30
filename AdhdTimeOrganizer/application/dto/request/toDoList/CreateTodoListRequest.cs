using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record CreateTodoListItemRequest : BaseCreateTodoListRequest, ICreateRequest<TodoListItem>
{
    [Required]
    public long TaskPriorityId { get; init; }

    [Required]
    public long TodoListId { get; init; }

    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }

    public TodoListItem ToEntity => new()
    {
        UserId = 0,
        ActivityId = ActivityId,
        TotalCount = TotalCount,
        Note = Note,
        SuggestedTime = SuggestedTime,
        TaskPriorityId = TaskPriorityId,
        TodoListId = TodoListId,
        DueDate = DueDate,
        DueTime = DueTime,
    };
}
