using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using mapper_UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;
using UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class UserGetByIdEndpoint(AppCommandDbContext dbContext, UserMapper mapper)
    : BaseGetByIdEndpoint<User, UserResponse, mapper_UserMapper>(dbContext, mapper)
{
    public override string[] AllowedRoles()
    {
        return EndpointHelper.GetAdminOrHigherRoles();
    }
}