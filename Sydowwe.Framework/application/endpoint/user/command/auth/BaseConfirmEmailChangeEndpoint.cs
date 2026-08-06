using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Confirms email change using the token sent to the new email address.
/// This is the second step of the email change process.
///
/// <para>Anonymous: the confirmation link is followed from the new mailbox, which may well be a
/// browser with no session. The token is the credential.</para>
/// </summary>
public abstract class BaseConfirmEmailChangeEndpoint<TUser>(
    UserManager<TUser> userManager,
    IRefreshTokenService refreshTokenService)
    : Endpoint<ConfirmEmailChangeRequest, EmptyResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/user/change-email/confirm");
        AllowAnonymous();
        Throttle(5, 60, TrustedIpMiddleware.ClientIpHeaderName);
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
                AddError(error.Description);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // The three steps below belong together and in this order: the login identifier moves to the
        // new address, the security stamp invalidates anything derived from the old one, and every
        // refresh token dies so no session minted against the old address survives the change.
        await userManager.SetUserNameAsync(user, req.NewEmail);

        await userManager.UpdateSecurityStampAsync(user);
        await refreshTokenService.RevokeAllUserTokensAsync(user.Id,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

        await Send.NoContentAsync(ct);
    }
}