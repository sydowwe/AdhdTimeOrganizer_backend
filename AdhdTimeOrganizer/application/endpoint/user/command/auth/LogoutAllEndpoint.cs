using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class LogoutAllEndpoint(IRefreshTokenService refreshTokenService)
    : BaseLogoutAllEndpoint(refreshTokenService)
{
}