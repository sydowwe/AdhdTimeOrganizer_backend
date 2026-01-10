using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.dto.response.activityPlanning.taskPlannerDayTemplate;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templateTask.command;

public class UpdateTemplatePlannerTaskEndpoint(AppCommandDbContext dbContext, TemplatePlannerTaskMapper mapper)
    : BaseUpdateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest, TemplatePlannerTaskMapper>(dbContext, mapper)
{
}
