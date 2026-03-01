using AdhdTimeOrganizer.application.dto.request.@base;

namespace AdhdTimeOrganizer.application.dto.request.toDoList;

public record TodoListRequest : NameTextIconRequest
{
    public long? CategoryId { get; init; }
}
