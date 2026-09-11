using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record UpdateTodoListItemRequest : BaseUpdateTodoListRequest, IUpdateRequest<TodoListItem>
{
    public long TaskPriorityId { get; init; }

    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// <c>{hours, minutes}</c>, per the portal's time-of-day convention — see the remarks on
    /// <see cref="CreateTodoListItemRequest.DueTime"/> for why this stopped being a <c>TimeOnly</c>.
    /// </summary>
    public TimeDto? DueTime { get; init; }

    public long? PairedLeisureActivityId { get; init; }

    public void UpdateEntity(TodoListItem e)
    {
        e.ActivityId = ActivityId;
        e.TotalCount = TotalCount;
        e.Note = Note;
        e.SuggestedTime = SuggestedTime;
        e.TaskPriorityId = TaskPriorityId;
        e.DueDate = DueDate;
        e.DueTime = DueTime?.ToTimeOnly();
        e.PairedLeisureActivityId = PairedLeisureActivityId;

        // IsDone is not written here, and DoneCount and DisplayOrder are not on the request at all
        // (see BaseUpdateTodoListRequest). The three of them used to be three independent assignments
        // in this method plus a fourth in AfterMapping, so a PUT could produce IsDone = true with every
        // step unticked, or DoneCount = 3 on an item whose steps were all open.
        //
        // "Done" has three representations — IsDone, DoneCount and each step's IsDone — and only
        // BaseTodoListItem.SetDone snaps all three together. It needs the steps to be in their final
        // shape first, which is only true after AfterMapping has reconciled them, so that is where the
        // completion write lives now.
    }
}