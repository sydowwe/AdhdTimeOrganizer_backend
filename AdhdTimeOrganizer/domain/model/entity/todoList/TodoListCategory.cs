using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public class TodoListCategory : BaseEntityWithUser, IBaseNameTextColorIconEntity
{
    public required string Name { get; set; }
    public string? Text { get; set; }
    public required string Color { get; set; }
    public string? Icon { get; set; }

    public ICollection<TodoList> TodoListColl { get; set; } = new List<TodoList>();
}