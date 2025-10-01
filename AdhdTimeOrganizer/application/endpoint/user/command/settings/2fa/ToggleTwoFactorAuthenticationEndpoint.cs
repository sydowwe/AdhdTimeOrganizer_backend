using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.preprocessor;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.profile;

/// <summary>
/// Enables or disables two-factor authentication for the user.
/// When enabling, returns QR code and recovery codes for setup.
/// </summary>
public class ToggleTwoFactorAuthEndpoint(
    UserManager<User> userManager,
    ITwoFactorAuthService twoFactorAuthService)
    : Endpoint<VerifyUserRequest, TwoFactorAuthResponse>
{
    public override void Configure()
    {
        Post("user/2fa/toggle");
        PreProcessor<VerifyUserPreProcessor<VerifyUserRequest>>();
        Summary(s => { s.Summary = "Enable or disable two-factor authentication"; });
    }

    public override async Task HandleAsync(VerifyUserRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser();

        // Toggle the 2FA state
        var newState = !user.TwoFactorEnabled;
        var result = await userManager.SetTwoFactorEnabledAsync(user, newState);
        
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Description);
            }
            await SendErrorsAsync(400, ct);
            return;
        }

        // If enabling 2FA, generate QR code and recovery codes
        if (newState)
        {
            var setupResult = await twoFactorAuthService.SetUpTwoFactorAuth(user);
            if (setupResult.Failed)
            {
                AddError("Failed to generate 2FA setup data");
                await SendErrorsAsync(500, ct);
                return;
            }

            await SendOkAsync(setupResult.Data, ct);
        }
        else
        {
            // 2FA disabled successfully
            await SendOkAsync(new TwoFactorAuthResponse { TwoFactorEnabled = false }, ct);
        }
    }
}
