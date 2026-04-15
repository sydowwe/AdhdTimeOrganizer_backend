namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoListStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public int Order { get; set; }
    public bool IsDone { get; set; }
    public string? Note { get; set; }
}
