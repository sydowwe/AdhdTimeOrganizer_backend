using FastEndpoints;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Exchanges the refresh-token cookie for a fresh access/refresh pair.
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseRefreshTokenEndpoint(IJwtService jwtService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
        // Key the limit on the real client IP. Without headerName the limit is keyed on the connection
        // IP, which behind a reverse proxy is the proxy itself — every caller would share one bucket.
        Throttle(10, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s => { s.Summary = "Refresh access token using refresh token from cookie (web clients)"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Web client - read from cookie
        var refreshToken = HttpContext.Request.Cookies[AuthCookies.RefreshTokenName];
        if (string.IsNullOrEmpty(refreshToken))
        {
            AddError("Refresh token not provided");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var result = await jwtService.RefreshTokensAsync(refreshToken, HttpContext);

        if (!result.Success)
        {
            AddError(result.Error ?? "Invalid or expired refresh token");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        jwtService.SetTokenCookies(HttpContext, result.AccessToken!, result.RefreshToken!, result.IsStayLoggedIn);
        await Send.NoContentAsync(ct);
    }
}