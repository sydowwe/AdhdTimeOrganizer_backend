using AdhdTimeOrganizer.application.dto.response.taskPlanner.template;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using TaskPlannerDayTemplateMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TaskPlannerDayTemplateMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetAllTaskPlannerDayTemplateEndpoint(AppCommandDbContext dbContext, TaskPlannerDayTemplateMapper mapper)
    : BaseGetAllEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateResponse, TaskPlannerDayTemplateMapper>(dbContext, mapper)
{
    protected override IQueryable<TaskPlannerDayTemplate> Sort(IQueryable<TaskPlannerDayTemplate> query)
        => query.OrderByDescending(t => t.LastUsedAt).ThenBy(t => t.Name);
}