using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;

public class PlannerTask : BasePlannerTask
{
    public required PlannerTaskStatus Status { get; set; } // Pending, InProgress, Completed, Skipped
    public TimeOnly? ActualStartTime { get; set; }
    public TimeOnly? ActualEndTime { get; set; }
    public string? SkipReason { get; set; }
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
    /// by every status-mutation call site (PatchPlannerTaskStatusEndpoint, TodoListItemIsDoneChangedEventHandler).
    /// Callers that also want to set actual times for InProgress/Completed do so after calling this.
    /// </summary>
    public void ApplyStatus(PlannerTaskStatus newStatus)
    {
        Status = newStatus;
        if (newStatus is PlannerTaskStatus.Cancelled or PlannerTaskStatus.NotStarted)
        {
            ActualStartTime = null;
            ActualEndTime = null;
        }
    }
}