using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

public class ValidateTwoFactorAuthForLoginEndpoint(
    UserManager<User> userManager,
    ITwoFactorAuthService twoFactorAuthService,
    IJwtService jwtService,
    IDataProtectionProvider dataProtectionProvider)
    : Endpoint<TwoFactorAuthLoginRequest>
{
    public override void Configure()
    {
        Post("/auth/login/2fa");
        Summary(s => { s.Summary = "Validate 2FA for login"; });
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Forwarded-For");
    }

    public override async Task HandleAsync(TwoFactorAuthLoginRequest request, CancellationToken ct)
    {
        var protector = dataProtectionProvider.CreateProtector("2fa-pending").ToTimeLimitedDataProtector();

        // Web: pending-2fa cookie; Extension: PendingAuthToken in body
        var rawToken = HttpContext.Request.Cookies["pending-2fa"] ?? request.PendingAuthToken;
        if (string.IsNullOrEmpty(rawToken))
        {
            AddError("Invalid or expired authentication session");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        long userId;
        try
        {
            var userIdStr = protector.Unprotect(rawToken);
            if (!long.TryParse(userIdStr, out userId))
                throw new InvalidOperationException();
        }
        catch
        {
            AddError("Invalid or expired authentication session");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            AddError("Invalid or expired authentication session");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var twoFactorResult = await twoFactorAuthService.ValidateToken(user, request.Token);
        if (twoFactorResult.Failed)
        {
            AddError("Invalid two-factor authentication token");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        // Consume the pending-2fa cookie
        HttpContext.Response.Cookies.Delete("pending-2fa", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth/login/2fa"
        });

        await jwtService.GenerateJwtAndSetAuthCookie(request.StayLoggedIn, AuthMethodEnum.Password, user, HttpContext);

        await Send.NoContentAsync(ct);
    }
}