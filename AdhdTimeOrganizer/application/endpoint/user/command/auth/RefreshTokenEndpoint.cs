using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class RefreshTokenEndpoint(IJwtService jwtService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
        Throttle(hitLimit: 10, durationSeconds: 60, headerName: "X-Real-IP");
        Summary(s => { s.Summary = "Refresh access token using refresh token from cookie (web clients)"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Web client - read from cookie
        var refreshToken = HttpContext.Request.Cookies["refresh-token"];
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
