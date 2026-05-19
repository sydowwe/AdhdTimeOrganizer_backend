using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class LogoutAllEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/auth/logout-all");
        Summary(s => { s.Summary = "Logout from all devices by revoking all refresh tokens"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await refreshTokenService.RevokeAllUserTokensAsync(userId, ipAddress);

        // Clear current session cookies
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

        HttpContext.Response.Cookies.Delete("session-hash", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api"
        });

        await Send.NoContentAsync(ct);
    }
}
