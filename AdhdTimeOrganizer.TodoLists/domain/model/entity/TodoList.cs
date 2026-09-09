using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

public class TodoList : BaseEntityWithUser, IBaseNameTextEntity
{
    public required string Name { get; set; }
    public string? Text { get; set; }
    public string? Icon { get; set; }

    public long? CategoryId { get; set; }
    public TodoListCategory? Category { get; set; }

    // No ItemCount / CompletedCount properties here on purpose (PERF-3). They existed as unmapped
    // computed properties over TodoListItemColl — a second, in-memory copy of what
    // TodoListResponse.Projection already computes in SQL, and reading one loads the whole collection
    // per list. The projection is the only source of those two numbers.
    public ICollection<TodoListItem> TodoListItemColl { get; set; } = new List<TodoListItem>();
}