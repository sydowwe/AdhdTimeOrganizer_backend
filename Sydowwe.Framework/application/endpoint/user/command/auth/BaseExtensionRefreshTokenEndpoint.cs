using FastEndpoints;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Token-body counterpart of <see cref="BaseRefreshTokenEndpoint"/>: takes the refresh token from the
/// request body and returns the rotated pair in the response body, rather than reading and writing
/// cookies.
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseExtensionRefreshTokenEndpoint(IJwtService jwtService)
    : Endpoint<RefreshTokenRequest, RefreshTokenResponse>
{
    public override void Configure()
    {
        Post("/auth/extension/refresh");
        AllowAnonymous();
        // Key the limit on the real client IP. Without headerName the limit is keyed on the connection
        // IP, which behind a reverse proxy is the proxy itself — every caller would share one bucket.
        Throttle(10, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s =>
        {
            s.Summary = "Refresh access token using refresh token from request body (extension clients)";
        });
    }

    public override async Task HandleAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.RefreshToken))
        {
            AddError("Refresh token not provided");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var result = await jwtService.RefreshTokensAsync(req.RefreshToken, HttpContext);

        if (!result.Success)
        {
            AddError(result.Error ?? "Invalid or expired refresh token");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        await Send.OkAsync(new RefreshTokenResponse
        {
            AccessToken = result.AccessToken!,
            RefreshToken = result.RefreshToken!
        }, ct);
    }
}
