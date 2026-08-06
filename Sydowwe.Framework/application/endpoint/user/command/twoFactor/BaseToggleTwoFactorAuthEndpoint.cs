using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.twoFactor;

/// <summary>
/// Enables or disables two-factor authentication for the user.
/// When enabling, returns the QR code and recovery codes for setup.
///
/// <para>Gated by <see cref="VerifyUserPreProcessor{TUser,TRequest}"/>: turning 2FA off is exactly the
/// move a hijacked session would make first, so the caller must re-prove password + current token.</para>
/// </summary>
public abstract class BaseToggleTwoFactorAuthEndpoint<TUser>(
    UserManager<TUser> userManager,
    ITwoFactorAuthService<TUser> twoFactorAuthService)
    : Endpoint<VerifyUserRequest, TwoFactorAuthResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/user/2fa/toggle");
        PreProcessor<VerifyUserPreProcessor<TUser, VerifyUserRequest>>();
        Summary(s => { s.Summary = "Enable or disable two-factor authentication"; });
    }

    public override async Task HandleAsync(VerifyUserRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser<TUser>();

        // Toggle the 2FA state
        var newState = !user.TwoFactorEnabled;
        var result = await userManager.SetTwoFactorEnabledAsync(user, newState);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                AddError(error.Description);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // If enabling 2FA, generate QR code and recovery codes
        if (newState)
        {
            var setupResult = await twoFactorAuthService.SetUpTwoFactorAuth(user);
            if (setupResult.Failed)
            {
                AddError("Failed to generate 2FA setup data");
                await Send.ErrorsAsync(500, ct);
                return;
            }

            await Send.OkAsync(setupResult.Data, ct);
        }
        else
        {
            // 2FA disabled successfully
            await Send.OkAsync(new TwoFactorAuthResponse { TwoFactorEnabled = false }, ct);
        }
    }
}