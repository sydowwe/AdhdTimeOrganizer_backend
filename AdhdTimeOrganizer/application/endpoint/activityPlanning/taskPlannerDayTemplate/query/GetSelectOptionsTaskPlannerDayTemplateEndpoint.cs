using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using TaskPlannerDayTemplateMapper = AdhdTimeOrganizer.application.mapper.activityPlanning.TaskPlannerDayTemplateMapper;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetSelectOptionsTaskPlannerDayTemplateEndpoint(AppCommandDbContext dbContext, TaskPlannerDayTemplateMapper mapper)
    : BaseGetSelectOptionsEndpoint<TaskPlannerDayTemplate, TaskPlannerDayTemplateMapper>(dbContext, mapper)
{
    public override IQueryable<TaskPlannerDayTemplate> Filter(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.Where(t => t.IsActive).OrderBy(t => t.Name);
    }
}
