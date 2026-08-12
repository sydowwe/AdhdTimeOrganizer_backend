using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record CreateTodoListItemRequest : BaseCreateTodoListRequest, ICreateRequest<TodoListItem>
{
    public long TaskPriorityId { get; init; }


    public long TodoListId { get; init; }

    public DateOnly? DueDate { get; init; }
    public TimeOnly? DueTime { get; init; }

    public long? PairedLeisureActivityId { get; init; }

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
        PairedLeisureActivityId = PairedLeisureActivityId
    };
}