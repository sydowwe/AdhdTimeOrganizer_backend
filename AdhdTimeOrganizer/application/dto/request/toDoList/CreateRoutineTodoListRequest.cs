using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record CreateRoutineTodoListRequest : BaseCreateTodoListRequest
{
    [Required]
    public long TimePeriodId { get; init; }
}