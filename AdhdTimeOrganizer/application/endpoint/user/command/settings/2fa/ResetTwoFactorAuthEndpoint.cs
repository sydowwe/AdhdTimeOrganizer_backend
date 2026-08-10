using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.application.endpoint.user.command.twoFactor;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings._2fa;

public class ResetTwoFactorAuthEndpoint(ITwoFactorAuthService<User> twoFactorAuthService)
    : BaseResetTwoFactorAuthEndpoint<User>(twoFactorAuthService)
{
}