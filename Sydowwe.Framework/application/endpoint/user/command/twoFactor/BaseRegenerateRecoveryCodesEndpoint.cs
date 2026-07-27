using FastEndpoints;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.twoFactor;

/// <summary>
/// Regenerates 2FA recovery codes. Invalidates all previous recovery codes.
/// Requires password and 2FA verification for security.
///
/// <para>Returns a bare <c>List&lt;string&gt;</c> rather than <c>TwoFactorAuthResponse</c> — the shape the
/// SPA already consumes, and there is no QR to report here.</para>
/// </summary>
public abstract class BaseRegenerateRecoveryCodesEndpoint<TUser>(ITwoFactorAuthService<TUser> twoFactorAuthService)
    : Endpoint<VerifyUserRequest, List<string>>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/user/2fa/recovery-codes/regenerate");
        PreProcessor<VerifyUserPreProcessor<TUser, VerifyUserRequest>>();
        Summary(s =>
        {
            s.Summary = "Regenerate 2FA recovery codes";
            s.Description = "Generates new recovery codes and invalidates all previous ones. Save these codes securely.";
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

        var result = await twoFactorAuthService.GenerateNewRecoveryCodes(user);
        if (result.Failed)
        {
            AddError("Failed to generate recovery codes");
            await Send.ErrorsAsync(500, ct);
            return;
        }

        await Send.OkAsync(result.Data.ToList(), ct);
    }
}
