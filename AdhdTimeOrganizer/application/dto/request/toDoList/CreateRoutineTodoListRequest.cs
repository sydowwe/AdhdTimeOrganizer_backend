using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.domain.model.entity.todoList;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record CreateRoutineTodoListRequest : BaseCreateTodoListRequest, ICreateRequest<RoutineTodoList>
{
    [Required]
    public long TimePeriodId { get; init; }
    public List<DayOfWeek> SuggestedDays { get; init; } = [];
    public int? SuggestedDayOfMonth { get; init; }

    public RoutineTodoList ToEntity => new()
    {
        UserId = 0,
        ActivityId = ActivityId,
        TotalCount = TotalCount,
        Note = Note,
        SuggestedTime = SuggestedTime,
        TimePeriodId = TimePeriodId,
        SuggestedDays = SuggestedDays,
        SuggestedDayOfMonth = SuggestedDayOfMonth,
    };
}
