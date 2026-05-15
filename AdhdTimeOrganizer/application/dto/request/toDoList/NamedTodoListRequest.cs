using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.todoList;

public record TodoListRequest : NameTextIconRequest
{
    public long? CategoryId { get; init; }
}
