using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings.email;

public class ConfirmEmailChangeEndpoint(
    UserManager<User> userManager,
    IRefreshTokenService refreshTokenService)
    : BaseConfirmEmailChangeEndpoint<User>(userManager, refreshTokenService)
{
}
