using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.templatePlannerTask.command;

public class CreateTemplatePlannerTaskEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TemplatePlannerTaskValidator>();
    }
}