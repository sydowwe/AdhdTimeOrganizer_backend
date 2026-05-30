using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.templatePlannerTask.query;

public class GetByIdTemplatePlannerTaskEndpoint(AppDbContext dbContext)
    : BaseGetByIdEndpoint<TemplatePlannerTask, TemplatePlannerTaskResponse>(dbContext)
{
}
