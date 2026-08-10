using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoList : BaseEntityWithUser, IBaseNameTextEntity
{
    public required string Name { get; set; }
    public string? Text { get; set; }
    public string? Icon { get; set; }

    public long? CategoryId { get; set; }
    public TodoListCategory? Category { get; set; }

    public ICollection<TodoListItem> TodoListItemColl { get; set; } = new List<TodoListItem>();

    public int ItemCount => TodoListItemColl.Count;
    public int CompletedCount => TodoListItemColl.Count(i => i.IsDone);
}