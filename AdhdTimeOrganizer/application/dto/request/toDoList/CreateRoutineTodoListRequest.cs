using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record CreateRoutineTodoListRequest : BaseCreateTodoListRequest
{
    [Required]
    public long TimePeriodId { get; init; }
    public List<DayOfWeek> SuggestedDays { get; init; } = [];
    public int? SuggestedDayOfMonth { get; init; }
}