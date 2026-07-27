using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Revokes every session the authenticated user holds except the one the request arrives on.
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseRevokeAllOtherSessionsEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("user/sessions/all");
        Summary(s => { s.Summary = "Revoke all sessions except the current one"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var currentSessionHash = HttpContext.Request.Cookies[AuthCookies.SessionHashName];
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        await refreshTokenService.RevokeAllExceptCurrentAsync(userId, currentSessionHash, ipAddress);

        await Send.NoContentAsync(ct);
    }
}
