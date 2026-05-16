using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using TaskPlannerDayTemplateMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TaskPlannerDayTemplateMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.command;

public class CreateTaskPlannerDayTemplateEndpoint(AppDbContext dbContext, TaskPlannerDayTemplateMapper mapper)
    : BaseCreateEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateRequest, TaskPlannerDayTemplateMapper>(dbContext, mapper)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskPlannerDayTemplateValidator>();
    }
}
