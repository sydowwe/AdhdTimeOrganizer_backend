using AdhdTimeOrganizer.Core.application.dto.request.extendable;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record BaseUpdateTodoListRequest : WithIsDoneRequest
{
    public long DisplayOrder { get; init; }

    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public string? Note { get; set; }
    public IntTime? SuggestedTime { get; set; }
    public List<TodoListStepRequest>? Steps { get; init; }
}