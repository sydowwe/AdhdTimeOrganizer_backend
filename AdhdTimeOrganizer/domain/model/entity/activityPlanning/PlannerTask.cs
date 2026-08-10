using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.Core.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class PlannerTask : BasePlannerTask
{
    public required PlannerTaskStatus Status { get; set; } // Pending, InProgress, Completed, Skipped
    public TimeOnly? ActualStartTime { get; set; }
    public TimeOnly? ActualEndTime { get; set; }
    public string? SkipReason { get; set; }
    public long? SourceTemplateTaskId { get; set; } // Track which template task this came from

    public string? GoogleEventId { get; set; }

    public long CalendarId { get; set; }
    public long? TodolistItemId { get; set; }

    public virtual Calendar Calendar { get; set; } = null!;
    public virtual TodoListItem? TodolistItem { get; set; }


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