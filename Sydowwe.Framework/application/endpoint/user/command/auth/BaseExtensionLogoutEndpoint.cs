using FastEndpoints;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Revokes the refresh token an extension client sends in the request body.
///
/// <para>⚠ Unlike <see cref="BaseLogoutEndpoint"/>, this is <b>not</b> anonymous, and the two must not
/// be harmonized. The web one acts on a cookie the browser attaches automatically and therefore has to
/// keep working once the access token has expired; this one reads the token out of a body the caller
/// composes, so requiring a valid access token costs nothing and keeps an arbitrary caller from
/// posting someone else's leaked refresh token to have it revoked.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseExtensionLogoutEndpoint(IRefreshTokenService refreshTokenService)
    : Endpoint<RefreshTokenRequest>
{
    public override void Configure()
    {
        Post("/auth/extension/logout");
        Summary(s => { s.Summary = "Logout extension client and revoke refresh token"; });
    }

    public override async Task HandleAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        // Extension sends the refresh token in the body. An empty one is a silent no-op — logout is
        // idempotent and must never fail a client that has already discarded its token.
        if (!string.IsNullOrEmpty(req.RefreshToken))
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await refreshTokenService.RevokeRefreshTokenAsync(req.RefreshToken, ipAddress);
        }

        await Send.NoContentAsync(ct);
    }
}
