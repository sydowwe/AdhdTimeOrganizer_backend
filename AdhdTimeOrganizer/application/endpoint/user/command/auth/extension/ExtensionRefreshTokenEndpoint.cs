using Sydowwe.Framework.application.endpoint.user.command.auth;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

public class ExtensionRefreshTokenEndpoint(IJwtService jwtService)
    : BaseExtensionRefreshTokenEndpoint(jwtService)
{
    // Nothing to configure: route, AllowAnonymous and throttling all come from the base.
}
