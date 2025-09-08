using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record TodoListRequest : WithIsDoneRequest
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    [Required]
    public long TaskUrgencyId { get; init; }
}