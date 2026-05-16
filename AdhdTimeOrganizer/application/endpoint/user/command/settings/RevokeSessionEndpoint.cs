using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class RevokeSessionEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
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
        var currentSessionHash = HttpContext.Request.Cookies["session-hash"];
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

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
