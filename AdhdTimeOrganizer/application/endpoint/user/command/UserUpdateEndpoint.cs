using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.helper;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using mapper_UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;
using UserMapper = AdhdTimeOrganizer.application.mapper.UserMapper;

namespace AdhdTimeOrganizer.application.endpoint.user.command;

public class UserUpdateEndpoint(AppCommandDbContext context, UserMapper mapper)
    : BaseUpdateEndpoint<User, UserRequest, UserResponse, mapper_UserMapper>(context, mapper)
{
    public override string[] AllowedRoles()
    {
        return EndpointHelper.GetAdminOrHigherRoles();
    }
}