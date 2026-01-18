using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.settings.email;

/// <summary>
/// Confirms email change using the token sent to the new email address.
/// This is the second step of the email change process.
/// </summary>
public class ConfirmEmailChangeEndpoint(
    UserManager<User> userManager,
    SignInManager<User> signInManager)
    : Endpoint<ConfirmEmailChangeRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("user/email/confirm");
        AllowAnonymous();
        Summary(s => { s.Summary = "Confirm email change with token"; });
    }

    public override async Task HandleAsync(ConfirmEmailChangeRequest req, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(req.UserId);
        if (user is null)
        {
            AddError("Invalid or expired confirmation link");
            await SendErrorsAsync(400, ct);
            return;
        }

        var result = await userManager.ChangeEmailAsync(user, req.NewEmail, req.Token);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Description);
            }
            await SendErrorsAsync(400, ct);
            return;
        }

        // Also update username to match new email (common pattern)
        await userManager.SetUserNameAsync(user, req.NewEmail);

        // Update security stamp to invalidate existing sessions
        // This forces re-login on all devices for security
        await userManager.UpdateSecurityStampAsync(user);
        
        // Sign out the user from current session
        await signInManager.SignOutAsync();

        await SendNoContentAsync(ct);
    }
}
