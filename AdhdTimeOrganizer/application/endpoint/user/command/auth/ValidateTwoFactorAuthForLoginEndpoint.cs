using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class ValidateTwoFactorAuthForLoginEndpoint(UserManager<User> userManager,ITwoFactorAuthService twoFactorAuthService, IJwtService jwtService) : Endpoint<TwoFactorAuthLoginRequest>
{
    public override void Configure()
    {
        Post("user/login-2fa");
        Summary(s => { s.Summary = "Validate 2FA for login"; });
        AllowAnonymous();
    }

    public override async Task HandleAsync(TwoFactorAuthLoginRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            await SendErrorsAsync(400, ct);
            return;
        }
        var twoFactorResult = await twoFactorAuthService.ValidateToken(user, request.Token);
        if (twoFactorResult.Failed)
        {
            AddError("Invalid two-factor authentication token");
            await SendErrorsAsync(401, ct);
            return;
        }

        await jwtService.GenerateJwtAndSetAuthCookie(request.StayLoggedIn, AuthMethodEnum.Password, user, userManager, HttpContext);

        await SendOkAsync(ct);
    }
}