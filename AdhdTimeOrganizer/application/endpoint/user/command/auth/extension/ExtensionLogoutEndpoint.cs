using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

public class ExtensionLogoutEndpoint(IRefreshTokenService refreshTokenService)
    : BaseExtensionLogoutEndpoint(refreshTokenService)
{
    // Nothing to configure: route, the authenticated-only stance (unlike the web LogoutEndpoint) and
    // the silent no-op on an empty token all come from the base.
}
