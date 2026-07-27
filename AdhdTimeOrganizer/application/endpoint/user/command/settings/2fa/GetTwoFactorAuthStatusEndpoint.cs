using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.endpoint.user.command.twoFactor;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings._2fa;

public class GetTwoFactorAuthStatusEndpoint(UserManager<User> userManager)
    : BaseGetTwoFactorAuthStatusEndpoint<User>(userManager)
{
}
