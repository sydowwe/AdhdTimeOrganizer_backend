using AdhdTimeOrganizer.application.dto.response.@base;

namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record TodoListResponse : NameTextIconResponse
{
    public TodoListCategoryResponse? Category { get; init; }
    public int ItemCount { get; init; }
    public int CompletedCount { get; init; }
}
