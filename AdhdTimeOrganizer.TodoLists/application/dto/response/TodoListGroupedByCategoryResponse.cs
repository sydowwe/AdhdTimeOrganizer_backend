using Sydowwe.Framework.application.dto.response.@base;

namespace AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;

public record TodoListGroupedByCategoryResponse : IMyResponse
{
    public TodoListCategoryResponse? Category { get; init; }
    public required IEnumerable<TodoListResponse> Items { get; init; }
}