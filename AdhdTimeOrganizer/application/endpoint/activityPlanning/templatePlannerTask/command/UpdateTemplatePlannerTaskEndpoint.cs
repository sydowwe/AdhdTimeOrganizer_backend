using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.command;

public class UpdateTemplatePlannerTaskEndpoint(AppDbContext dbContext)
    : BaseUpdateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TemplatePlannerTaskValidator>();
    }
}
