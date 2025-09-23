using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record CreateRoutineTodoListRequest : BaseUpdateTodoListRequest
{

    [Required]
    public long TimePeriodId { get; init; }
}