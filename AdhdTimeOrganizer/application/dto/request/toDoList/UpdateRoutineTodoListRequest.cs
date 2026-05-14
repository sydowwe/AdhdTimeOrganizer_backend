using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.domain.helper;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record UpdateRoutineTodoListRequest : BaseUpdateTodoListRequest
{
    [Required]
    public long TimePeriodId { get; init; }
    public List<DayOfWeek> SuggestedDays { get; init; } = [];
    public int? SuggestedDayOfMonth { get; init; }
}