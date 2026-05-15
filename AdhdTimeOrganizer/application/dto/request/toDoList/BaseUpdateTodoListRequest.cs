using AdhdTimeOrganizer.application.dto.request.extendable;
using AdhdTimeOrganizer.domain.helper;

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