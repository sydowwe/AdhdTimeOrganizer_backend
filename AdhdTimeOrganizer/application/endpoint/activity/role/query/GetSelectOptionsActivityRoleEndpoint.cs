using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GetSelectOptionsActivityRoleEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<ActivityRole>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<ActivityRole> query)
    {
        return query.Select(a => new SelectOptionResponse
        {
            Id = a.Id,
            Text = a.Name
        });
    }
}
