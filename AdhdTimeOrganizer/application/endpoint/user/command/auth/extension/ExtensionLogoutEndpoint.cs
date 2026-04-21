using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

public class ExtensionLogoutEndpoint(IRefreshTokenService refreshTokenService)
    : Endpoint<RefreshTokenRequest>
{
    public override void Configure()
    {
        Post("auth/extension/logout");
        Summary(s => { s.Summary = "Logout extension client and revoke refresh token"; });
    }

    public override async Task HandleAsync(RefreshTokenRequest req, CancellationToken ct)
    {
        // Extension sends refresh token in body
        if (!string.IsNullOrEmpty(req.RefreshToken))
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await refreshTokenService.RevokeRefreshTokenAsync(req.RefreshToken, ipAddress);
        }

        await Send.OkAsync(ct);
    }
}
