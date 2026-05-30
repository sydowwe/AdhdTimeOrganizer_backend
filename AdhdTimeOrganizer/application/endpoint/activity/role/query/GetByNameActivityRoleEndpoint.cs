using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GetByNameActivityRoleEndpoint(AppDbContext dbContext)
    : BaseGetByFieldEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
    protected override string FieldName => nameof(ActivityRole.Name);

    protected override IQueryable<ActivityRole> FilterByField(IQueryable<ActivityRole> query, string value)
    {
        return query.Where(ar => ar.Name == value);
    }
}