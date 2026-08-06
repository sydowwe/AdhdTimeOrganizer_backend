using FastEndpoints;
using Sydowwe.Framework.application.extensions;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Revokes one of the authenticated user's sessions by id.
///
/// <para>The two failure codes are distinct on purpose and clients branch on them: 404 when no such
/// session belongs to the user, 400 when the id names the caller's own current session.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseRevokeSessionEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("user/sessions/{id:long:required}");
        Summary(s => { s.Summary = "Revoke a specific session by ID (cannot revoke the current session)"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sessionId = Route<long>("id");
        var userId = User.GetId();
        var currentSessionHash = HttpContext.Request.Cookies[AuthCookies.SessionHashName];
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var (found, isCurrent) = await refreshTokenService.RevokeSessionByIdAsync(sessionId, userId, currentSessionHash, ipAddress);

        if (!found)
        {
            AddError("Session not found.");
            await Send.ErrorsAsync(404, ct);
            return;
        }

        if (isCurrent)
        {
            AddError("Cannot revoke the current session. Use logout instead.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}