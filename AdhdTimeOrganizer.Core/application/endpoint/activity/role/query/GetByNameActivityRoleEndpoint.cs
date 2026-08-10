using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using Sydowwe.Framework.application.endpoint.@base.read;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.endpoint.activity.role.query;

public class GetByNameActivityRoleEndpoint(DbContext dbContext)
    : BaseGetByFieldEndpoint<ActivityRole, ActivityRoleResponse>(dbContext)
{
    protected override string FieldName => nameof(ActivityRole.Name);

    protected override IQueryable<ActivityRole> FilterByField(IQueryable<ActivityRole> query, string value)
    {
        return query.Where(ar => ar.Name == value);
    }
}