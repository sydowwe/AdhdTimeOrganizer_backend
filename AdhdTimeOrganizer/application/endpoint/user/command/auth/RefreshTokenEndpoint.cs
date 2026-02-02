using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class RefreshTokenEndpoint(IJwtService jwtService) : EndpointWithoutRequest<RefreshTokenResponse>
{
    public override void Configure()
    {
        Post("auth/refresh");
        AllowAnonymous();
        Summary(s => { s.Summary = "Refresh access token using refresh token from cookie (web clients)"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Web client - read from cookie
        var refreshToken = HttpContext.Request.Cookies["refresh-token"];
        if (string.IsNullOrEmpty(refreshToken))
        {
            AddError("Refresh token not provided");
            await SendErrorsAsync(401, ct);
            return;
        }

        try
        {
            var (accessToken, newRefreshToken, isStayLoggedIn) = await jwtService.RefreshTokensAsync(
                refreshToken, isExtensionClient: false, HttpContext);

            jwtService.SetTokenCookies(HttpContext, accessToken, newRefreshToken, isStayLoggedIn);

            await SendNoContentAsync(ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            AddError(ex.Message);
            await SendErrorsAsync(401, ct);
        }
    }
}
