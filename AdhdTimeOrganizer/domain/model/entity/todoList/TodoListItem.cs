namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoListItem : BaseTodoListItem
{
    public required long TaskPriorityId { get; set; }
    public TaskPriority TaskPriority { get; set; } = null!;

    public DateOnly? DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }

    public long? TodoListId { get; set; }
    public TodoList? TodoList { get; set; }
}
