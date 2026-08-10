using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Planning.application.endpoint.activityPlanning.taskPlannerDayTemplate.query;

public class GetSelectOptionsTaskPlannerDayTemplateEndpoint(DbContext dbContext)
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