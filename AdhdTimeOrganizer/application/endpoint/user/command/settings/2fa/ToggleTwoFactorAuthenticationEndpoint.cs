using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.twoFactor;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings._2fa;

public class ToggleTwoFactorAuthEndpoint(
    UserManager<User> userManager,
    ITwoFactorAuthService<User> twoFactorAuthService)
    : BaseToggleTwoFactorAuthEndpoint<User>(userManager, twoFactorAuthService)
{
}