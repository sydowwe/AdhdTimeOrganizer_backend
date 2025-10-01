using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.preprocessor;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// Resets the authenticator app by generating a new QR code and secret key.
/// This invalidates the old authenticator setup. Requires password and 2FA verification.
/// </summary>
public class ResetTwoFactorAuthEndpoint(ITwoFactorAuthService twoFactorAuthService)
    : Endpoint<VerifyUserRequest, AuthenticatorSetupResponse>
{
    public override void Configure()
    {
        Post("user/2fa/reset");
        PreProcessor<VerifyUserPreProcessor<VerifyUserRequest>>();
        Summary(s =>
        {
            s.Summary = "Reset authenticator app (generate new QR code)";
            s.Description = "Generates a new secret key and QR code. The old authenticator setup will stop working.";
        });
    }

    public override async Task HandleAsync(VerifyUserRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser();

        // Verify 2FA is actually enabled
        if (!user.TwoFactorEnabled)
        {
            AddError("Two-factor authentication is not enabled");
            await SendErrorsAsync(400, ct);
            return;
        }

        var qrResult = await twoFactorAuthService.GenerateNewQrCode(user);
        if (qrResult.Failed)
        {
            AddError("Failed to generate QR code");
            await SendErrorsAsync(500, ct);
            return;
        }

        // Also generate new recovery codes since authenticator is being reset
        var recoveryResult = await twoFactorAuthService.GenerateNewRecoveryCodes(user);
        if (recoveryResult.Failed)
        {
            AddError("Failed to generate recovery codes");
            await SendErrorsAsync(500, ct);
            return;
        }

        await SendOkAsync(new AuthenticatorSetupResponse
        {
            QrCode = qrResult.Data,
            RecoveryCodes = recoveryResult.Data.ToList()
        }, ct);
    }
}
