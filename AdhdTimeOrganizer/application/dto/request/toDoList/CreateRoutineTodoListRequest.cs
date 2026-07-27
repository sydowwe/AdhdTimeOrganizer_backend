using AdhdTimeOrganizer.domain.model.entity.todoList;
using Sydowwe.Framework.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record CreateRoutineTodoListRequest : BaseCreateTodoListRequest, ICreateRequest<RoutineTodoList>
{
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
        SuggestedDayOfMonth = SuggestedDayOfMonth
    };
}