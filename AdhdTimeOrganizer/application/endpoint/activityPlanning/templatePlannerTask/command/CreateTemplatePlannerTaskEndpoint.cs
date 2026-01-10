using AdhdTimeOrganizer.application.dto.request.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.application.mapper;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.templateTask.command;

public class CreateTemplatePlannerTaskEndpoint(AppCommandDbContext dbContext, TemplatePlannerTaskMapper mapper)
    : BaseCreateEndpoint<TemplatePlannerTask, TemplatePlannerTaskRequest, TemplatePlannerTaskMapper>(dbContext, mapper)
{
}
