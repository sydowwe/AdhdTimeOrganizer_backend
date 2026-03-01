namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoListItem : BaseTodoList
{
    public required long TaskPriorityId { get; set; }
    public TaskPriority TaskPriority { get; set; } = null!;

    public long? TodoListId { get; set; }
    public TodoList? TodoList { get; set; }
}
