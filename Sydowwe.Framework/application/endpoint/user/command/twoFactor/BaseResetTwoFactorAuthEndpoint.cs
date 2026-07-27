using FastEndpoints;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.twoFactor;

/// <summary>
/// Resets the authenticator app by generating a new QR code and secret key.
/// This invalidates the old authenticator setup. Requires password and 2FA verification.
///
/// <para>Recovery codes are regenerated as well as the QR — both, in that order, in one call: the old
/// codes were minted against the secret that just stopped existing.</para>
///
/// <para>Returns <see cref="TwoFactorAuthResponse"/> rather than a setup-only DTO: it is a superset of
/// what this route needs, and a second near-duplicate response record is exactly what the framework
/// reconciliation removed. <c>qrCode</c> / <c>recoveryCodes</c> keep their wire names; the extra
/// <c>twoFactorEnabled</c> field is additive for clients.</para>
/// </summary>
public abstract class BaseResetTwoFactorAuthEndpoint<TUser>(ITwoFactorAuthService<TUser> twoFactorAuthService)
    : Endpoint<VerifyUserRequest, TwoFactorAuthResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/user/2fa/reset");
        PreProcessor<VerifyUserPreProcessor<TUser, VerifyUserRequest>>();
        Summary(s =>
        {
            s.Summary = "Reset authenticator app (generate new QR code)";
            s.Description = "Generates a new secret key and QR code. The old authenticator setup will stop working.";
        });
    }

    public override async Task HandleAsync(VerifyUserRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser<TUser>();

        // Verify 2FA is actually enabled
        if (!user.TwoFactorEnabled)
        {
            AddError("Two-factor authentication is not enabled");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var qrResult = await twoFactorAuthService.GenerateNewQrCode(user);
        if (qrResult.Failed)
        {
            AddError("Failed to generate QR code");
            await Send.ErrorsAsync(500, ct);
            return;
        }

        // Also generate new recovery codes since authenticator is being reset
        var recoveryResult = await twoFactorAuthService.GenerateNewRecoveryCodes(user);
        if (recoveryResult.Failed)
        {
            AddError("Failed to generate recovery codes");
            await Send.ErrorsAsync(500, ct);
            return;
        }

        await Send.OkAsync(new TwoFactorAuthResponse
        {
            // Always true — the guard above returned 400 for anyone with 2FA disabled.
            TwoFactorEnabled = true,
            QrCode = qrResult.Data,
            RecoveryCodes = recoveryResult.Data.ToList()
        }, ct);
    }
}
