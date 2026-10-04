using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

public class PlannerTask : BasePlannerTask
{
    public required PlannerTaskStatus Status { get; set; } // Pending, InProgress, Completed, Skipped
    public TimeOnly? ActualStartTime { get; set; }
    public TimeOnly? ActualEndTime { get; set; }
    public string? SkipReason { get; set; }

    /// <summary>A built-in skip reason, by key. At most one of this and <see cref="SkipReason"/> is non-null.</summary>
    public SkipReasonKey? SkipReasonKey { get; set; }

    public long? SourceTemplateTaskId { get; set; } // Track which template task this came from

    public string? GoogleEventId { get; set; }

    /// <summary>
    /// The task closes itself once its slot is over: an <c>InProgress</c> task carrying this is completed by
    /// <c>CompleteFinishedPlannerTasksJobHandler</c> when its <c>EndTime</c> passes on the user's clock.
    /// <para>
    /// For blocks that do not need a tick to have happened — rest booked from the leisure picker's "do it
    /// now" is the one client today. Without it that block stayed <c>InProgress</c> for good: it held the
    /// home now-bar for the rest of the day and landed in the plan-vs-actual trend as "never happened".
    /// Set at creation only; a <c>NotStarted</c> task is never closed, since nobody said it began.
    /// </para>
    /// </summary>
    public bool AutoCompleteAtEnd { get; set; }

    /// <summary>
    /// The day the task was first planned for, set the first time it is carried over
    /// (<c>CarryOverPlannerTaskEndpoint</c>) and never changed after; null for a task that never slipped. Moving a
    /// task by hand (PUT, drag) is a re-plan, not a slip, and leaves this alone.
    /// </summary>
    public DateOnly? OriginalDate { get; set; }

    /// <summary>
    /// How many times the task has been carried over to a later day. "Pushed four times" is the cue to split the
    /// task, shrink it or drop it — which the plain move the client used to do threw away.
    /// </summary>
    public int CarryOverCount { get; set; }

    public long CalendarId { get; set; }

    /// <summary>
    /// The to-do item this task was planned from, if any. A real FK to <c>todo_list_item</c> with
    /// <c>ON DELETE SET NULL</c> — but deliberately <b>id-only, with no navigation property</b>.
    /// <para>
    /// <c>TodoListItem</c> lives in <c>AdhdTimeOrganizer.TodoLists</c>, and this slice does not
    /// reference that project. A navigation here would be the only thing forcing that reference, and
    /// nothing ever used it: every call site — <c>PatchPlannerTaskStatusEndpoint</c>,
    /// <c>ApplyTemplatePlannerTaskEndpoint</c>, <c>PlannerTaskResponse</c>, and the two
    /// <c>*IsDoneChangedEvent</c> handlers — carries the bare id and looks the item up on the
    /// TodoLists side. The relationship itself is declared host-side, where both types are visible,
    /// in <c>AppDbContext.ConfigureCrossSliceRelationships</c>; see the comment there for why the
    /// constraint name is pinned.
    /// </para>
    /// <para>
    /// So: do not add <c>public virtual TodoListItem? TodolistItem</c> back. It re-imposes a
    /// Planning → TodoLists project reference to buy a navigation no code path wants.
    /// </para>
    /// </summary>
    public long? TodolistItemId { get; set; }

    public virtual Calendar Calendar { get; set; } = null!;


    public bool IsDone => Status == PlannerTaskStatus.Completed;
    public string Color => Activity.Role.Color;

    /// <summary>
    /// Sets Status and, for Cancelled/NotStarted, clears the actual start/end times — the reset shared
    /// by every status-mutation call site (PatchPlannerTaskStatusEndpoint, TodoListItemIsDoneChangedEventHandler,
    /// RoutineTodoListIsDoneChangedEventHandler).
    /// Callers that also want to set actual times for InProgress/Completed do so after calling this.
    /// <para>
    /// A skip reason only ever sits on a <c>Cancelled</c> task: a transition to <c>Cancelled</c> stores
    /// <paramref name="skipReason"/> (trimmed, blank ⇒ null — a re-skip overwrites), and any other status clears
    /// it, so a restored task does not keep the reason for a skip that no longer holds.
    /// </para>
    /// <para>
    /// <see cref="SkipReasonKey"/> follows the same rules, and at most one of the two is set. A
    /// <paramref name="skipReasonKey"/> wins (the validator rejects sending both); free text equal to a built-in
    /// label exactly is stored as its key (<see cref="SkipReasonKeyLabels"/>), so the six labels never come
    /// back as text whichever client wrote them.
    /// </para>
    /// </summary>
    public void ApplyStatus(PlannerTaskStatus newStatus, string? skipReason = null, SkipReasonKey? skipReasonKey = null)
    {
        Status = newStatus;
        SkipReason = null;
        SkipReasonKey = null;
        if (newStatus == PlannerTaskStatus.Cancelled)
        {
            if (skipReasonKey is not null)
                SkipReasonKey = skipReasonKey;
            else if (!string.IsNullOrWhiteSpace(skipReason))
            {
                var text = skipReason.Trim();
                if (SkipReasonKeyLabels.TryGetKey(text, out var key))
                    SkipReasonKey = key;
                else
                    SkipReason = text;
            }
        }
        if (newStatus is PlannerTaskStatus.Cancelled or PlannerTaskStatus.NotStarted)
        {
            ActualStartTime = null;
            ActualEndTime = null;
        }
    }
}