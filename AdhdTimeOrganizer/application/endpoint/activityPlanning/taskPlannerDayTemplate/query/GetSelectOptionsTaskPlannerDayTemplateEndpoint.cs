using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetSelectOptionsTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseGetSelectOptionsEndpoint<TaskPlannerDayTemplate>(dbContext)
{
    protected override IQueryable<TaskPlannerDayTemplate> Filter(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.Where(t => t.IsActive).OrderBy(t => t.Name);
    }

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TaskPlannerDayTemplate> query) =>
        query.Select(t => new SelectOptionResponse
        {
            Id = t.Id,
            Text = t.Name,
        });
}