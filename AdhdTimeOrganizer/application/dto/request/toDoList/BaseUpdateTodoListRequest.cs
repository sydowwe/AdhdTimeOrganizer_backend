using System.ComponentModel.DataAnnotations;
using AdhdTimeOrganizer.application.dto.request.extendable;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record BaseUpdateTodoListRequest : WithIsDoneRequest
{
    public long DisplayOrder { get; init; }

    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
}