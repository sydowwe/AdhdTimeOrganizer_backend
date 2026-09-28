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