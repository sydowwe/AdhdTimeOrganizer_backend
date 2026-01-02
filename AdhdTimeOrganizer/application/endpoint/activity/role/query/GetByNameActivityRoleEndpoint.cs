using System.Linq.Expressions;
using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GetByNameActivityRoleEndpoint(AppCommandDbContext dbContext, ActivityRoleMapper mapper)
    : BaseGetByFieldEndpoint<ActivityRole, ActivityRoleResponse, ActivityRoleMapper>(dbContext, mapper)
{
    protected override string FieldName => nameof(ActivityRole.Name);
    protected override Expression<Func<ActivityRole, bool>> FilterQuery(string value)
    {
        return ar => ar.Name == value;
    }
}
