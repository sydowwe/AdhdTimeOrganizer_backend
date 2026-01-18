namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoList : BaseTodoList
{

    public required long TaskPriorityId { get; set; }
    public TaskPriority TaskPriority { get; set; } = null!;
}