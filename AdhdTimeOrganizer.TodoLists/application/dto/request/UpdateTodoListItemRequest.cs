using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record UpdateTodoListItemRequest : BaseUpdateTodoListRequest, IUpdateRequest<TodoListItem>
{
    public long TaskPriorityId { get; init; }

    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }

    public long? PairedLeisureActivityId { get; init; }

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
        e.PairedLeisureActivityId = PairedLeisureActivityId;
    }
}