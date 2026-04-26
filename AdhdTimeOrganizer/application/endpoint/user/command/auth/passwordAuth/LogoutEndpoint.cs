using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

public class LogoutEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("auth/logout");
        Summary(s => { s.Summary = "Logout a user and revoke refresh token"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Get refresh token (check cookie)
        var refreshToken = HttpContext.Request.Cookies["refresh-token"];

        if (!string.IsNullOrEmpty(refreshToken))
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            await refreshTokenService.RevokeRefreshTokenAsync(refreshToken, ipAddress);
        }

        // Clear cookies
        HttpContext.Response.Cookies.Delete("auth-token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });

        HttpContext.Response.Cookies.Delete("refresh-token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth"
        });

        await Send.NoContentAsync(ct);
    }
}