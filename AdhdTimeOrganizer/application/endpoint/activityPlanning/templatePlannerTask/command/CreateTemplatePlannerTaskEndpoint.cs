using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Core.application.validator;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.command;

public class CreateTemplatePlannerTaskEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TemplatePlannerTaskValidator>();
    }
}