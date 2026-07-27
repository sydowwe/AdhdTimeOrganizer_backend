using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Revokes every refresh token the authenticated user holds and clears the current session cookies.
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseLogoutAllEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/auth/logout-all");
        Summary(s => { s.Summary = "Logout from all devices by revoking all refresh tokens"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await refreshTokenService.RevokeAllUserTokensAsync(userId, ipAddress);

        // Clear current session cookies
        HttpContext.Response.ClearSessionCookies();

        await Send.NoContentAsync(ct);
    }
}
