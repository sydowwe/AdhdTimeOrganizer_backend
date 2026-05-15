using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.preprocessor;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

public class ChangePasswordEndpoint(
    UserManager<User> userManager,
    IRefreshTokenService refreshTokenService)
    : Endpoint<ChangePasswordRequest, EmptyResponse>
{
    public override void Configure()
    {
        Patch("user/password");
        PreProcessor<VerifyUserPreProcessor<ChangePasswordRequest>>();
        Summary(s => {
            s.Summary = "Change user password";
            s.Description = "Returns 401 if 2FA verification fails or current password is incorrect";
        });
    }

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser();

        var result = await userManager.ChangePasswordAsync(user, req.Password, req.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Description);
            }
            await Send.ErrorsAsync(401, ct);
            return;
        }

        await refreshTokenService.RevokeAllUserTokensAsync(user.Id);

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
