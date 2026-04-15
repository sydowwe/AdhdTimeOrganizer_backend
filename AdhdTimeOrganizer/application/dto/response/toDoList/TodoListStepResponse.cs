namespace AdhdTimeOrganizer.application.dto.response.toDoList;

public record TodoListStepResponse
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public int Order { get; init; }
    public bool IsDone { get; init; }
    public string? Note { get; init; }
}
