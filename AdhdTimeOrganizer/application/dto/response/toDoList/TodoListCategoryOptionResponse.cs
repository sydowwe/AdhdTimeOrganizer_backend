namespace AdhdTimeOrganizer.application.dto.response.todoList;

public record TodoListCategoryOptionResponse
{
    public long? CategoryId { get; init; }
    public required string Name { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
}
