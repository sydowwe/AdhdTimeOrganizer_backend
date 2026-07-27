using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

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