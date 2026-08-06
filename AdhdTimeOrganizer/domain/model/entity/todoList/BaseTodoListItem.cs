using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entityInterface;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.domain.model.entity.todoList;

public abstract class BaseTodoListItem : BaseEntityWithIsDone, IEntityWithDoneAndTotalCount
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public long DisplayOrder { get; set; }
    public string? Note { get; set; }
    public IntTime? SuggestedTime { get; set; }
    public ICollection<TodoListStep> Steps { get; set; } = [];
}