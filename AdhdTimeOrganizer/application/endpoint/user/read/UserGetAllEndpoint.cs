using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.endpoint.@base.read;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using mapper_UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;
using UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class UserGetAllEndpoint(AppCommandDbContext dbContext, UserMapper mapper)
    : BaseGetAllEndpoint<User, UserResponse, mapper_UserMapper>(dbContext, mapper)
{
    public override string[] AllowedRoles()
    {
        return EndpointHelper.GetAdminOrHigherRoles();
    }
}