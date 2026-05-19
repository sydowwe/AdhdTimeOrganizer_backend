using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.passwordAuth;

public class ValidateTwoFactorAuthForLoginExtensionEndpoint(
    ITwoFactorAuthService twoFactorAuthService,
    IJwtService jwtService)
    : Endpoint<TwoFactorAuthExtensionLoginRequest>
{
    public override void Configure()
    {
        Post("/auth/login/2fa/extension");
        Summary(s => { s.Summary = "Validate 2FA for login (extension — body token)"; });
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Real-IP");
    }

    public override async Task HandleAsync(TwoFactorAuthExtensionLoginRequest request, CancellationToken ct)
    {
        var result = await twoFactorAuthService.ValidatePendingLoginToken(request.PendingAuthToken, request.Token, ct);
        if (result.Failed)
        {
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(401, ct);
            return;
        }

        await jwtService.GenerateJwtAndSetAuthCookie(request.StayLoggedIn, AuthMethodEnum.Password, result.Data, HttpContext);
        await Send.NoContentAsync(ct);
    }
}
