using AdhdTimeOrganizer.application.dto.request.activity;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record BaseCreateTodoListRequest : ActivityIdRequest
{
    public int? TotalCount { get; set; }
}