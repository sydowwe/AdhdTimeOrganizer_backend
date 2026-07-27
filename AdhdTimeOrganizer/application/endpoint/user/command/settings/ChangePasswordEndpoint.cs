using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class ChangePasswordEndpoint(
    UserManager<User> userManager,
    IRefreshTokenService refreshTokenService)
    : BaseChangePasswordEndpoint<User>(userManager, refreshTokenService)
{
}