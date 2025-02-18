using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.Command.application.dto.request.extendable;

namespace AdhdTimeOrganizer.Command.application.dto.request.toDoList;

public record RoutineToDoListRequest : WithIsDoneRequest
{
    [Required]
    public long TimePeriodId { get; init; }
}