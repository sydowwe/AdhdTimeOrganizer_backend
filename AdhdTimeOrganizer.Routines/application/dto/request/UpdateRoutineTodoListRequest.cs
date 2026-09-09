using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.Routines.application.dto.request.todoList;

public record UpdateRoutineTodoListRequest : BaseUpdateTodoListRequest, IUpdateRequest<RoutineTodoList>
{
    public long TimePeriodId { get; init; }
    public List<DayOfWeek> SuggestedDays { get; init; } = [];
    public int? SuggestedDayOfMonth { get; init; }

    public void UpdateEntity(RoutineTodoList e)
    {
        e.ActivityId = ActivityId;
        e.TotalCount = TotalCount;
        e.Note = Note;
        e.SuggestedTime = SuggestedTime;
        e.TimePeriodId = TimePeriodId;
        e.SuggestedDays = SuggestedDays;
        e.SuggestedDayOfMonth = SuggestedDayOfMonth;

        // IsDone arrives on the inherited request shape but is deliberately not mapped; DoneCount and
        // DisplayOrder are not on the shape at all (see BaseUpdateTodoListRequest). All three are
        // server-owned on a routine item.
        //
        // Completion belongs to the toggle endpoint: BaseToggleIsDoneTodoListEndpoint reconciles IsDone,
        // DoneCount and every step together, RoutineToggleIsDoneTodoListEndpoint then advances the item
        // streak and publishes RoutineTodoListIsDoneChangedEvent — the only channel by which Planning learns
        // a routine was completed. Completing a routine through this PUT instead would leave the paired
        // planner task stale and the three representations of "done" out of step, silently, because
        // cross-slice handlers log and swallow. DoneCount is re-derived from TotalCount in the endpoint's
        // AfterMapping rather than taken from the body.
        //
        // DisplayOrder is per-period and derived server-side (the create path and
        // ChangeDisplayOrderRoutineTodoListEndpoint, which groups on TimePeriodId); AfterMapping re-derives
        // it when this edit moves the item to another period.
    }
}