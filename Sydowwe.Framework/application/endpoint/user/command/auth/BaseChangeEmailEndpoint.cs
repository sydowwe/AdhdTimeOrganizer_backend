using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.preprocessor;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// First step of the email change: verifies the new address is free and mails a change token to it.
/// Nothing is persisted here — the address only changes once
/// <see cref="BaseConfirmEmailChangeEndpoint{TUser}"/> consumes that token, so an address the caller
/// does not control can never be attached to the account.
///
/// <para>Gated by <see cref="VerifyUserPreProcessor{TUser,TRequest}"/>: an authenticated session alone
/// is not enough, because the login address is what recovers the account.</para>
/// </summary>
public abstract class BaseChangeEmailEndpoint<TUser>(
    UserManager<TUser> userManager,
    IUserEmailSenderService<TUser> emailSender)
    : Endpoint<ChangeEmailRequest, EmptyResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Patch("user/change-email");
        PreProcessor<VerifyUserPreProcessor<TUser, VerifyUserRequest>>();
        Summary(s => { s.Summary = "Change user email address"; });
    }

    public override async Task HandleAsync(ChangeEmailRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser<TUser>();

        // Check if email is already taken
        var existingUser = await userManager.FindByEmailAsync(req.NewEmail);
        if (existingUser is not null)
        {
            AddError(r => r.NewEmail, "Email address is already in use");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Generate email change token and send confirmation email
        var token = await userManager.GenerateChangeEmailTokenAsync(user, req.NewEmail);

        await emailSender.SendEmailChangeConfirmationAsync(user, req.NewEmail, token);

        await Send.NoContentAsync(ct);
    }
}
