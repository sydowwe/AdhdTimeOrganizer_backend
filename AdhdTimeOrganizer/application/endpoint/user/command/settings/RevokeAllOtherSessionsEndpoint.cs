using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class RevokeAllOtherSessionsEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("user/sessions/all");
        Summary(s => { s.Summary = "Revoke all sessions except the current one"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var currentSessionHash = HttpContext.Request.Cookies["session-hash"];
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        await refreshTokenService.RevokeAllExceptCurrentAsync(userId, currentSessionHash, ipAddress);

        await Send.NoContentAsync(ct);
    }
}
