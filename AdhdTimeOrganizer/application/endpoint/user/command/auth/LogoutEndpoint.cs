using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class LogoutEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("user/logout");
        Summary(s => { s.Summary = "Logout a user"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Clear the auth cookie
        HttpContext.Response.Cookies.Delete("auth-token", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });

        await SendOkAsync(ct);
    }
}