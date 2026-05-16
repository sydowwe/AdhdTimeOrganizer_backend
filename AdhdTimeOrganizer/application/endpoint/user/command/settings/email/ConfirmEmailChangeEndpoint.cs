using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
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
    IRefreshTokenService refreshTokenService)
    : Endpoint<ConfirmEmailChangeRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("/user/change-email/confirm");
        AllowAnonymous();
        Throttle(hitLimit: 5, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => { s.Summary = "Confirm email change with token"; });
    }

    public override async Task HandleAsync(ConfirmEmailChangeRequest req, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(req.UserId.ToString());
        if (user is null)
        {
            AddError("Invalid or expired confirmation link");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var result = await userManager.ChangeEmailAsync(user, req.NewEmail, req.Token);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                AddError(error.Description);
            }
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Also update username to match new email (common pattern)
        await userManager.SetUserNameAsync(user, req.NewEmail);

        await userManager.UpdateSecurityStampAsync(user);
        await refreshTokenService.RevokeAllUserTokensAsync(user.Id);

        await Send.NoContentAsync(ct);
    }
}
