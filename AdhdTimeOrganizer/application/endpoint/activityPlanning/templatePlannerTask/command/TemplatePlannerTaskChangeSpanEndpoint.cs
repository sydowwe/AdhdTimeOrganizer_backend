using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.command;

public class TemplatePlannerTaskChangeSpanEndpoint(AppDbContext dbContext) : BasePatchEndpoint<TemplatePlannerTask, PlannerTaskChangeSpanRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskChangeSpanValidator>();
    }

    protected override void Mapping(TemplatePlannerTask entity, PlannerTaskChangeSpanRequest req)
    {
        entity.StartTime = new TimeOnly(req.StartTime.Hours, req.StartTime.Minutes);
        entity.EndTime = new TimeOnly(req.EndTime.Hours, req.EndTime.Minutes);
    }
}