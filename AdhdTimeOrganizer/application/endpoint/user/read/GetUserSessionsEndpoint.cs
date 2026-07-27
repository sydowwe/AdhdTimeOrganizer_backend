using Sydowwe.Framework.application.endpoint.user.read;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.read;

public class GetUserSessionsEndpoint(IRefreshTokenService refreshTokenService)
    : BaseGetUserSessionsEndpoint(refreshTokenService)
{
}
