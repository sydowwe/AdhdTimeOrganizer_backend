using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.domain.helper;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record BaseCreateTodoListRequest : ActivityIdRequest
{
    public int? TotalCount { get; set; }
    public string? Note { get; set; }
    public MyIntTime? SuggestedTime { get; set; }
    public List<TodoListStepRequest>? Steps { get; init; }
}