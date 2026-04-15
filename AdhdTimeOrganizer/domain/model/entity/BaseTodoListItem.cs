using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.@base;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity;

public abstract class BaseTodoListItem : BaseEntityWithIsDone, IEntityWithDoneAndTotalCount
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public long DisplayOrder { get; set; }
    public string? Note { get; set; }
    public MyIntTime? SuggestedTime { get; set; }
    public ICollection<TodoListStep> Steps { get; set; } = [];
}