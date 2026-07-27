using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.settings;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class DeleteUserAccountEndpoint(UserManager<User> userManager)
    : BaseDeleteUserAccountEndpoint<User>(userManager);
