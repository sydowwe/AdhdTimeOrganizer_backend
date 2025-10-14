using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class PlannerTask : BaseEntityWithIsDone
{
    public required TimeOnly StartTime { get; set; }
    public required TimeOnly EndTime { get; set; }
    public required bool IsBackground { get; set; }
    public required bool IsOptional { get; set; }

    public TaskStatus Status { get; set; } // Pending, InProgress, Completed, Skipped
    public TimeOnly? ActualStartTime { get; set; }
    public TimeOnly? ActualEndTime { get; set; }

    // Template relationship
    public bool IsFromTemplate { get; set; }
    public long? SourceTemplateTaskId { get; set; } // Track which template task this came from

    // Notes
    public string? Location { get; set; } // "Home", "Gym", "Office"
    public string? Notes { get; set; }
    public string? SkipReason { get; set; }

    public long CalendarId { get; set; }
    public virtual Calendar Calendar { get; set; } = null!;
    public long? PriorityId { get; set; }
    public TaskPriority? Priority { get; set; }
    public long? TodolistId { get; set; }
    public virtual TodoList? Todolist { get; set; }

    public string Color => Activity.Role.Color;
    public int EstimatedMinuteLength => (int) (StartTime - EndTime).TotalMinutes;
}
