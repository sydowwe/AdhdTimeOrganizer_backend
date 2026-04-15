using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class PlannerTask : BasePlannerTask
{
    public required PlannerTaskStatus Status { get; set; } // Pending, InProgress, Completed, Skipped
    public TimeOnly? ActualStartTime { get; set; }
    public TimeOnly? ActualEndTime { get; set; }
    public string? SkipReason { get; set; }
    public long? SourceTemplateTaskId { get; set; } // Track which template task this came from

    public required long CalendarId { get; set; }
    public long? TodolistItemId { get; set; }

    public virtual Calendar Calendar { get; set; } = null!;
    public virtual TodoListItem? TodolistItem { get; set; }


    public bool IsDone => Status == PlannerTaskStatus.Completed;
    public string Color => Activity.Role.Color;
}
