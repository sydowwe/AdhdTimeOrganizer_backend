using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record TodoListGroupedByCategoryResponse : IMyResponse
{
    public TodoListCategoryResponse? Category { get; init; }
    public required IEnumerable<TodoListResponse> Items { get; init; }
}
