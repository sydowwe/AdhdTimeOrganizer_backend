using AdhdTimeOrganizer.application.dto.request.taskPlanner;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.plannerTask.command;

public class CreatePlannerTaskEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<PlannerTask, PlannerTaskRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<PlannerTaskValidator>();
    }
}