using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using TemplatePlannerTaskMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TemplatePlannerTaskMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.command;

public class UpdateTemplatePlannerTaskEndpoint(AppDbContext dbContext, TemplatePlannerTaskMapper mapper)
    : BaseUpdateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest, TemplatePlannerTaskMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TemplatePlannerTaskValidator>();
    }
}
