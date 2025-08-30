using AdhdTimeOrganizer.application.endpoint.@base.command;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.user.command;

public class UserDeleteEndpoint(AppCommandDbContext context)
    : BaseDeleteEndpoint<User>(context)
{
}