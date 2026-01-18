namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TemplatePlannerTask : BasePlannerTask
{
    public required long TemplateId { get; set; }
    public TaskPlannerDayTemplate Template { get; set; } = null!;
}