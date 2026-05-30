using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.category.query;

public class GetSelectOptionsActivityCategoryEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<ActivityCategory>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<ActivityCategory> query)
    {
        return query.Select(a => new SelectOptionResponse
        {
            Id = a.Id,
            Text = a.Name
        });
    }
}
