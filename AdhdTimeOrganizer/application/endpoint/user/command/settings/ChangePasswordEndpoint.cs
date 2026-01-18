using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings;

/// <summary>
/// Changes user password. Requires current password and optionally 2FA token.
/// Invalidates all existing sessions after successful change.
/// </summary>
public class ChangePasswordEndpoint(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    ITwoFactorAuthService twoFactorAuthService)
    : Endpoint<ChangePasswordRequest, EmptyResponse>
{
    public override void Configure()
    {
        Patch("user/password");
        Summary(s => { s.Summary = "Change user password"; });
    }

    public override async Task HandleAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            await SendUnauthorizedAsync(ct);
            return;
        }

        // Verify 2FA token if enabled
        var twoFactorResult = await twoFactorAuthService.ValidateToken(user, req.TwoFactorAuthToken);
        if (twoFactorResult.Failed)
        {
            AddError("Invalid two-factor authentication token");
            await SendErrorsAsync(401, ct);
            return;
        }

        // Change password (this also verifies current password)
        var result = await userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                // Map specific error codes to fields for better UX
                if (error.Code == "PasswordMismatch")
                {
                    AddError(r => r.CurrentPassword, "Current password is incorrect");
                }
                else
                {
                    AddError(error.Description);
                }
            }
            await SendErrorsAsync(400, ct);
            return;
        }

        // Update security stamp to invalidate all existing sessions
        // Forces user to re-login on all devices for security
        await userManager.UpdateSecurityStampAsync(user);
        
        // Sign out from current session
        await signInManager.SignOutAsync();

        await SendNoContentAsync(ct);
    }
}
