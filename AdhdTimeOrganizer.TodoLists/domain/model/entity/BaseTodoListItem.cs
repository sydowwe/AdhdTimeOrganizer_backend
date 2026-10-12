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

    /// <summary>
    /// One tick of the item's checkbox: <paramref name="forceValue"/> <c>true</c> ticks it (one more repetition of a
    /// counted item, done once the count is reached), <c>false</c> unticks it (one fewer), <c>null</c> flips it. Steps
    /// follow the item. The rule behind every "toggle is done" endpoint and the to-do push's Done button, in one place.
    /// </summary>
    public void ApplyIsDoneToggle(bool? forceValue = null)
    {
        if (TotalCount.HasValue)
        {
            if (forceValue == false)
            {
                if (IsDone)
                {
                    DoneCount = TotalCount - 1;
                    IsDone = false;
                }
                else if (DoneCount > 0)
                {
                    DoneCount--;
                }

                ResetSteps();
            }
            else if (forceValue == true)
            {
                DoneCount = (DoneCount ?? 0) + 1;
                if (DoneCount >= TotalCount)
                {
                    DoneCount = TotalCount;
                    IsDone = true;
                }

                ResetSteps();
            }
            else
            {
                if (IsDone)
                {
                    DoneCount = 0;
                    IsDone = false;
                    ResetSteps();
                }
                else
                {
                    DoneCount = (DoneCount ?? 0) + 1;
                    // >=, not ==, and clamped — matching the forced arm above and ApplyStepToggle. An item
                    // whose DoneCount has overshot its TotalCount would otherwise never tick to done; it
                    // would just keep incrementing, one increment further out of range on every tap.
                    if (DoneCount >= TotalCount)
                    {
                        DoneCount = TotalCount;
                        IsDone = true;
                    }

                    ResetSteps();
                }
            }
        }
        else
        {
            IsDone = forceValue ?? !IsDone;
            if (Steps.Count != 0)
                foreach (var step in Steps)
                    step.IsDone = IsDone;
        }
    }

    private void ResetSteps()
    {
        foreach (var step in Steps)
            step.IsDone = IsDone;
    }
}