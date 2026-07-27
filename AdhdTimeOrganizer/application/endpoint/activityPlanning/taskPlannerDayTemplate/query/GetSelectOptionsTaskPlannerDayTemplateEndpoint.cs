using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetSelectOptionsTaskPlannerDayTemplateEndpoint(AppDbContext dbContext)
    : BaseGetSelectOptionsEndpoint<TaskPlannerDayTemplate>(dbContext)
{
    public override IQueryable<TaskPlannerDayTemplate> Filter(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.Where(t => t.IsActive).OrderBy(t => t.Name);
    }

    protected override IQueryable<SelectOptionResponse> Map(IQueryable<TaskPlannerDayTemplate> query)
    {
        return query.Select(t => new SelectOptionResponse
        {
            Id = t.Id,
            Text = t.Name
        });
    }
}