using AdhdTimeOrganizer.domain.model.entity.activity;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TemplatePlannerTask : BasePlannerTask
{
    public long TemplateId { get; set; }
    public virtual TaskPlannerDayTemplate Template { get; set; } = null!;
}