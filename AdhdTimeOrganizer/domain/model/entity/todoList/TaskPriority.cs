using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TaskPriority : BaseTextColorEntity
{
    public int Priority { get; set; }
    public ICollection<TodoListItem> TodoListColl { get; set; } = new List<TodoListItem>();
}