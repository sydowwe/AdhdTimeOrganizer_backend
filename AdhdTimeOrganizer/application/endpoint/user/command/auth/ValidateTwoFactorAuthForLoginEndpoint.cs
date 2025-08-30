using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.result;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class ValidateTwoFactorAuthForLoginEndpoint(SignInManager<User> signInManager) : Endpoint<TwoFactorAuthLoginRequest>
{
    public override void Configure()
    {
        Post("user/validate-2fa");
        Summary(s => { s.Summary = "Validate 2FA for login"; });
    }

    public override async Task HandleAsync(TwoFactorAuthLoginRequest request, CancellationToken ct)
    {
        var result =
            await signInManager.TwoFactorAuthenticatorSignInAsync(request.Token, request.StayLoggedIn,
                false);
        if (!result.Succeeded) Result.Error(ResultErrorType.InternalServerError, result.ToString());

        await SendOkAsync(ct);
    }
}