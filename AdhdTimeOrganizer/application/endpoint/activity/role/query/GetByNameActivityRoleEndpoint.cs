using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activity.role.query;

public class GetByNameActivityRoleEndpoint(AppCommandDbContext dbContext, ActivityRoleMapper mapper)
    : BaseGetByNameEndpoint<ActivityRole, ActivityRoleResponse, ActivityRoleMapper>(dbContext, mapper)
{
}
