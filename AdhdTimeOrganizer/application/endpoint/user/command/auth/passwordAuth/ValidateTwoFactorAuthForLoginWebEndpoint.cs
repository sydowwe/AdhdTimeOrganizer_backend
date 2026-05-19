using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

public class ValidateTwoFactorAuthForLoginWebEndpoint(
    ITwoFactorAuthService twoFactorAuthService,
    IJwtService jwtService)
    : Endpoint<TwoFactorAuthLoginRequest>
{
    public override void Configure()
    {
        Post("/auth/login/2fa");
        Summary(s => { s.Summary = "Validate 2FA for login (web — cookie-based)"; });
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Real-IP");
    }

    public override async Task HandleAsync(TwoFactorAuthLoginRequest request, CancellationToken ct)
    {
        var rawToken = HttpContext.Request.Cookies["pending-2fa"];
        if (string.IsNullOrEmpty(rawToken))
        {
            AddError("Invalid or expired authentication session");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var result = await twoFactorAuthService.ValidatePendingLoginToken(rawToken, request.Token, ct);
        if (result.Failed)
        {
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(401, ct);
            return;
        }

        HttpContext.Response.Cookies.Delete("pending-2fa", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth/login/2fa"
        });

        await jwtService.GenerateJwtAndSetAuthCookie(request.StayLoggedIn, AuthMethodEnum.Password, result.Data, HttpContext);
        await Send.NoContentAsync(ct);
    }
}
