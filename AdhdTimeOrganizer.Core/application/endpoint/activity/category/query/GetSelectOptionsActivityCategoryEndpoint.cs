using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.response.generic;
using Sydowwe.Framework.application.endpoint.@base.read;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.category.query;

public class GetSelectOptionsActivityCategoryEndpoint(
    DbContext appDbContext)
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