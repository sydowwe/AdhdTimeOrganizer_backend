using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.endpoint.@base.read.withoutUser;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using mapper_UserRoleMapper = AdhdTimeOrganizer.application.mapper.UserRoleMapper;
using UserRoleMapper = AdhdTimeOrganizer.application.mapper.UserRoleMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class UserRoleGetAllSelectOptionsEndpoint(AppCommandDbContext appDbContext, UserRoleMapper mapper)
    : BaseWithoutUserGetSelectOptionsEndpoint<UserRole, mapper_UserRoleMapper>(appDbContext, mapper)
{
}