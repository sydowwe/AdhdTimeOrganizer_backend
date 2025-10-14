using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TemplateTask : BaseEntityWithActivity
{
    public required TimeOnly StartTime { get; set; }
    public required TimeOnly EndTime { get; set; }
    public required bool IsBackground { get; set; }
    public required bool IsOptional { get; set; }

    public string? Location { get; set; }
    public string? Notes { get; set; }

    public long TemplateId { get; set; }
    public virtual TaskPlannerDayTemplate Template { get; set; } = null!;
    public long? PriorityId { get; set; }
    public TaskPriority? Priority { get; set; }
}