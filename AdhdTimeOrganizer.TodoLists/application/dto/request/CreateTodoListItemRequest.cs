using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record CreateTodoListItemRequest : BaseCreateTodoListRequest, ICreateRequest<TodoListItem>
{
    public long TaskPriorityId { get; init; }


    public long TodoListId { get; init; }

    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// Time-of-day as <see cref="TimeDto"/> — <c>{hours, minutes}</c> — which is the portal's convention
    /// (CLAUDE.md; Planning, History and Tracking all express time-of-day this way). The entity keeps
    /// <c>TimeOnly</c>; the conversion belongs at this boundary.
    /// <para>
    /// This was a bare <c>TimeOnly</c> until 2026-09-11, the only such property left in any portal DTO.
    /// It serialised as the string <c>"17:00:00"</c> into an SPA that parses every time-of-day as
    /// <c>{hours, minutes}</c>, so every due time read back as 00:00 and the zeroed object was then
    /// rejected on write. The backend was self-consistent throughout, which is why nothing here failed.
    /// </para>
    /// </summary>
    public TimeDto? DueTime { get; init; }

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
        DueTime = DueTime?.ToTimeOnly(),
        PairedLeisureActivityId = PairedLeisureActivityId
    };
}