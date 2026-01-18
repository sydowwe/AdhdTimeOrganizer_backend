using System.ComponentModel.DataAnnotations;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record UpdateRoutineTodoListRequest : BaseUpdateTodoListRequest
{

    [Required]
    public long TimePeriodId { get; init; }
}