using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class ChangePasswordEndpoint(
    UserManager<User> userManager,
    ITwoFactorAuthService twoFactorAuthService,
    IRefreshTokenService refreshTokenService)
    : Endpoint<ChangePasswordRequest, EmptyResponse>
{
    public override void Configure()
    {
        Patch("user/password");
        Summary(s => { s.Summary = "Change user password"; });
    }

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        var twoFactorResult = await twoFactorAuthService.ValidateToken(user, req.TwoFactorAuthToken);
        if (twoFactorResult.Failed)
        {
            AddError("Invalid two-factor authentication token");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var result = await userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                if (error.Code == "PasswordMismatch")
                    AddError(r => r.CurrentPassword, "Current password is incorrect");
                else
                    AddError(error.Description);
            }
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Revoke all refresh tokens so stolen tokens can't be used after a password change
        await refreshTokenService.RevokeAllUserTokensAsync(user.Id);

        // Clear the current session cookies
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
