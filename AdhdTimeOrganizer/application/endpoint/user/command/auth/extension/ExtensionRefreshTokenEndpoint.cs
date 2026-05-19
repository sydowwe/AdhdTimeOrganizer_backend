using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.extension;

public class ExtensionRefreshTokenEndpoint(IJwtService jwtService)
    : Endpoint<RefreshTokenRequest, RefreshTokenResponse>
{
    public override void Configure()
    {
        Post("/auth/extension/refresh");
        AllowAnonymous();
        Throttle(hitLimit: 10, durationSeconds: 60, headerName: "X-Real-IP");
        Summary(s => { s.Summary = "Refresh access token using refresh token from request body (extension clients)"; });
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
