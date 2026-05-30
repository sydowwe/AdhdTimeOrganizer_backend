using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record UpdateRoutineTodoListRequest : BaseUpdateTodoListRequest, IUpdateRequest<RoutineTodoList>
{
    [Required]
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
