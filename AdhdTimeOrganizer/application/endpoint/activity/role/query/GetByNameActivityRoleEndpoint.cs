using AdhdTimeOrganizer.application.dto.response.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Sydowwe.Framework.application.endpoint.@base.read;

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