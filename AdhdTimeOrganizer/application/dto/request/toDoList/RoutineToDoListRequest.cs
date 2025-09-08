using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record RoutineTodoListRequest : WithIsDoneRequest
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    [Required]
    public long TimePeriodId { get; init; }
}