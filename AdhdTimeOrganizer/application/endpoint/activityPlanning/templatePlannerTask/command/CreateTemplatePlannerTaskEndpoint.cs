using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using TemplatePlannerTaskMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TemplatePlannerTaskMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.command;

public class CreateTemplatePlannerTaskEndpoint(AppCommandDbContext dbContext, TemplatePlannerTaskMapper mapper)
    : BaseCreateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest, TemplatePlannerTaskMapper>(dbContext, mapper)
{
}
