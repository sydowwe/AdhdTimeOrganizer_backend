using FastEndpoints;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Revokes the refresh token carried by the session cookie and clears the auth cookies.
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseLogoutEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("auth/logout");
        // Anonymous on purpose: the endpoint authenticates nothing — it acts on whatever refresh token
        // the cookie carries and no-ops when there isn't one. Requiring an access token would 401 a
        // caller whose token already expired, leaving the refresh token live and the cookies set.
        AllowAnonymous();
        Summary(s => { s.Summary = "Logout a user and revoke refresh token"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Get refresh token (check cookie)
        var refreshToken = HttpContext.Request.Cookies[AuthCookies.RefreshTokenName];

        if (!string.IsNullOrEmpty(refreshToken))
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await refreshTokenService.RevokeRefreshTokenAsync(refreshToken, ipAddress);
        }

        // Clear cookies. Delete attributes (path + SameSite + Secure) must match the set cookie
        // or the browser ignores the deletion — AuthCookies guarantees they line up.
        HttpContext.Response.ClearSessionCookies();

        await Send.NoContentAsync(ct);
    }
}