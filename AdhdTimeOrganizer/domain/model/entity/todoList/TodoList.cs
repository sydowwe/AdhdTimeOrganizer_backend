using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoList : BaseNameTextEntity
{
    public string? Icon { get; set; }

    public long? CategoryId { get; set; }
    public TodoListCategory? Category { get; set; }

    public ICollection<TodoListItem> TodoListItemColl { get; set; } = new List<TodoListItem>();

    public int ItemCount => TodoListItemColl.Count;
    public int CompletedCount => TodoListItemColl.Count(i => i.IsDone);
}
