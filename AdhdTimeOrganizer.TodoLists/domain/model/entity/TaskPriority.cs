using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

public class TaskPriority : BaseEntityWithUser, IBaseTextColorEntity
{
    public required string Text { get; set; }
    public required string Color { get; set; }
    public int Priority { get; set; }
    public ICollection<TodoListItem> TodoListColl { get; set; } = new List<TodoListItem>();
}