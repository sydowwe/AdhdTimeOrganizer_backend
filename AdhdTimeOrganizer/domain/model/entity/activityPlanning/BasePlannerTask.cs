using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public abstract class BasePlannerTask : BaseEntityWithActivity
{
    // public bool IsNextDay { get; set; } = false;
    public required TimeOnly StartTime { get; set; }
    public required TimeOnly EndTime { get; set; }
    public required bool IsBackground { get; set; }
    public required bool IsOptional { get; set; }

    // Notes
    public string? Location { get; set; } // "Home", "Gym", "Office"
    public string? Notes { get; set; }

    public long? PriorityId { get; set; }
    public TaskPriority? Priority { get; set; }
}