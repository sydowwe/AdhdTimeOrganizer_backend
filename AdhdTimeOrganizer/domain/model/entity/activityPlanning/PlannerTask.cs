using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.model.entityInterface;
using AdhdTimeOrganizer.domain.model.@enum;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class PlannerTask : BasePlannerTask, IEntityWithIsDone
{
    public required bool IsDone { get; set; }
    public required PlannerTaskStatus Status { get; set; } // Pending, InProgress, Completed, Skipped
    public TimeOnly? ActualStartTime { get; set; }
    public TimeOnly? ActualEndTime { get; set; }
    public string? SkipReason { get; set; }

    // Template relationship
    public bool IsFromTemplate { get; set; }
    public long? SourceTemplateTaskId { get; set; } // Track which template task this came from

    public required long CalendarId { get; set; }
    public virtual Calendar Calendar { get; set; } = null!;

    public long? TodolistId { get; set; }
    public virtual TodoList? Todolist { get; set; }


    public string Color => Activity.Role.Color;
    public int EstimatedMinuteLength => (int) (StartTime - EndTime).TotalMinutes;
}
