using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using TaskPlannerDayTemplateMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TaskPlannerDayTemplateMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetByIdTaskPlannerDayTemplateEndpoint(AppCommandDbContext dbContext, TaskPlannerDayTemplateMapper mapper)
    : BaseGetByIdEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse, TaskPlannerDayTemplateMapper>(dbContext, mapper)
{
}
