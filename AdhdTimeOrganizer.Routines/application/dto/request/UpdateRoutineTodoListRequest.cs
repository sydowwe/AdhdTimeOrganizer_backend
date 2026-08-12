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
        e.IsDone = IsDone;
        e.DisplayOrder = DisplayOrder;
        e.DoneCount = DoneCount;
        e.TotalCount = TotalCount;
        e.Note = Note;
        e.SuggestedTime = SuggestedTime;
        e.TimePeriodId = TimePeriodId;
        e.SuggestedDays = SuggestedDays;
        e.SuggestedDayOfMonth = SuggestedDayOfMonth;
    }
}