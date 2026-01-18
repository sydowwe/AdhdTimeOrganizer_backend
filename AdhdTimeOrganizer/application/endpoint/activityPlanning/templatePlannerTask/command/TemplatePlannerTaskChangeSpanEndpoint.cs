using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.command;

public class TemplatePlannerTaskChangeSpanEndpoint(AppCommandDbContext dbContext) : BasePatchEndpoint<TemplatePlannerTask, PlannerTaskChangeSpanRequest, TemplatePlannerTaskResponse>(dbContext)
{
    protected override void Mapping(TemplatePlannerTask entity, PlannerTaskChangeSpanRequest req)
    {
        entity.StartTime = new TimeOnly(req.StartTime.Hours, req.StartTime.Minutes);
        entity.EndTime = new TimeOnly(req.EndTime.Hours, req.EndTime.Minutes);
    }
}