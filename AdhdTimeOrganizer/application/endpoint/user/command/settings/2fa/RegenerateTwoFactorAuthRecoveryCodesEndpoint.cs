using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.preprocessor;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using FastEndpoints;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings._2fa;

/// <summary>
/// Regenerates 2FA recovery codes. Invalidates all previous recovery codes.
/// Requires password and 2FA verification for security.
/// </summary>
public class RegenerateRecoveryCodesEndpoint(ITwoFactorAuthService twoFactorAuthService)
    : Endpoint<VerifyUserRequest, List<string>>
{
    public override void Configure()
    {
        Post("user/2fa/recovery-codes/regenerate");
        PreProcessor<VerifyUserPreProcessor<VerifyUserRequest>>();
        Summary(s =>
        {
            s.Summary = "Regenerate 2FA recovery codes";
            s.Description = "Generates new recovery codes and invalidates all previous ones. Save these codes securely.";
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

        var result = await twoFactorAuthService.GenerateNewRecoveryCodes(user);
        if (result.Failed)
        {
            AddError("Failed to generate recovery codes");
            await SendErrorsAsync(500, ct);
            return;
        }

        await SendOkAsync(result.Data.ToList(), ct);
    }
}
