using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.Core.domain.model.entityInterface;
using Sydowwe.Framework.domain.valueObject;

namespace AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;

public abstract class BaseTodoListItem : BaseEntityWithIsDone, IEntityWithDoneAndTotalCount
{
    public int? DoneCount { get; set; }
    public int? TotalCount { get; set; }
    public long DisplayOrder { get; set; }
    public string? Note { get; set; }
    public IntTime? SuggestedTime { get; set; }
    public ICollection<TodoListStep> Steps { get; set; } = [];

    /// <summary>
    /// Snaps IsDone, DoneCount (when step-counted) and every step's IsDone together, so the three
    /// representations of "done" can't drift apart the way they did when callers set them separately.
    /// </summary>
    public void SetDone(bool isDone)
    {
        IsDone = isDone;
        if (TotalCount.HasValue)
            DoneCount = isDone ? TotalCount : 0;
        foreach (var step in Steps)
            step.IsDone = isDone;
    }
}