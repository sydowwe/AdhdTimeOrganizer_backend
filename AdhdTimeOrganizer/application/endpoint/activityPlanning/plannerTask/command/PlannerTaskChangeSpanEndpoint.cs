using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.dto.response.activityPlanning;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlanner.command;

public class PlannerTaskChangeSpanEndpoint(AppCommandDbContext dbContext) : BasePatchEndpoint<PlannerTask, PlannerTaskChangeSpanRequest, PlannerTaskResponse>(dbContext)
{
    protected override void Mapping(PlannerTask entity, PlannerTaskChangeSpanRequest req)
    {
        entity.StartTime = new TimeOnly(req.StartTime.Hours, req.StartTime.Minutes);
        entity.EndTime = new TimeOnly(req.EndTime.Hours, req.EndTime.Minutes);
    }
}