using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record UpdateTodoListItemRequest : BaseUpdateTodoListRequest, IUpdateRequest<TodoListItem>
{
    [Required]
    public long TaskPriorityId { get; init; }

    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }

    public void UpdateEntity(TodoListItem e)
    {
        e.ActivityId = ActivityId;
        e.IsDone = IsDone;
        e.DisplayOrder = DisplayOrder;
        e.DoneCount = DoneCount;
        e.TotalCount = TotalCount;
        e.Note = Note;
        e.SuggestedTime = SuggestedTime;
        e.TaskPriorityId = TaskPriorityId;
        e.DueDate = DueDate;
        e.DueTime = DueTime;
    }
}
