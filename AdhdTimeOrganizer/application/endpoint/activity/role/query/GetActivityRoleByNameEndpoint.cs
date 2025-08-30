using AdhdTimeOrganizer.application.dto.response;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.mapper.activity;
using AdhdTimeOrganizer.application.mapper.@interface;
using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityRole.read;

public class GetActivityRoleByNameEndpoint(AppCommandDbContext dbContext, ActivityRoleMapper mapper)
    : BaseGetByNameEndpoint<ActivityRole, RoleResponse, ActivityRoleMapper>(dbContext, mapper)
{
}
