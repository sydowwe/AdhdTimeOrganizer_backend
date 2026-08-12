using AdhdTimeOrganizer.Core.application.dto.request.activity;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.TodoLists.application.dto.request.todoList;

public record BaseCreateTodoListRequest : ActivityIdRequest
{
    public int? TotalCount { get; set; }
    public string? Note { get; set; }
    public IntTime? SuggestedTime { get; set; }
    public List<StepRequest>? Steps { get; init; }
}