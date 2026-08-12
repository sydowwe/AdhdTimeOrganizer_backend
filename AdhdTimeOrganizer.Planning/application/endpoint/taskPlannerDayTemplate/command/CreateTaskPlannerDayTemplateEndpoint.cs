using AdhdTimeOrganizer.Planning.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.Planning.application.validator;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.endpoint.@base.command;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.command;

public class CreateTaskPlannerDayTemplateEndpoint(DbContext dbContext)
    : BaseCreateEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateRequest>(dbContext)
{
    public override void Configure()
    {
        base.Configure();
        Validator<TaskPlannerDayTemplateValidator>();
    }
}