using AdhdTimeOrganizer.application.dto.request.activity;
using AdhdTimeOrganizer.application.dto.request.@interface;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record BaseCreateTodoListRequest : ActivityIdRequest
{
    public int? TotalCount { get; set; }
}