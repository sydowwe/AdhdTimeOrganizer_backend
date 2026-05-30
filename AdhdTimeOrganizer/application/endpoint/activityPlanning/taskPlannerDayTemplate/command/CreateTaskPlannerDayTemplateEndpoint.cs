using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.validator;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.command;

public class CreateTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseCreateEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskPlannerDayTemplateValidator>();
    }
}
