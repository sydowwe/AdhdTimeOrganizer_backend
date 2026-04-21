using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;
using System.Security.Claims;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class LogoutAllEndpoint(IRefreshTokenService refreshTokenService) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("auth/logout-all");
        Summary(s => { s.Summary = "Logout from all devices by revoking all refresh tokens"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !long.TryParse(userIdClaim, out var userId))
        {
            AddError("User not authenticated");
            await Send.ErrorsAsync(401, ct);
            return;
        }

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

        await Send.OkAsync(ct);
    }
}
