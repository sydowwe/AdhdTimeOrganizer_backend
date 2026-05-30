using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.activity.query;

public class GetSelectOptionsActivityEndpoint(
    AppDbContext appDbContext)
    : BaseGetSelectOptionsEndpoint<Activity>(appDbContext)
{
    protected override IQueryable<SelectOptionResponse> Map(IQueryable<Activity> query)
    {
        return query.Select(a => new SelectOptionResponse
        {
            Id = a.Id,
            Text = a.Name
        });
    }
}
