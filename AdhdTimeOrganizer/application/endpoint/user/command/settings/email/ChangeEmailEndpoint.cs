using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.helper;
using AdhdTimeOrganizer.application.preprocessor;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.profile;

/// <summary>
/// Initiates email change process by sending a confirmation link to the new email address.
/// Requires password and optionally 2FA token for verification.
/// </summary>
public class ChangeEmailEndpoint(
    UserManager<User> userManager,
    IUserEmailSenderService emailSender,
    IConfiguration configuration,
    ITwoFactorAuthService twoFactorAuthService)
    : Endpoint<ChangeEmailRequest, EmptyResponse>
{
    public override void Configure()
    {
        Patch("user/email");
        PreProcessor<VerifyUserPreProcessor<VerifyUserRequest>>();
        Summary(s => { s.Summary = "Change user email address"; });
    }

    public override async Task HandleAsync(ChangeEmailRequest req, CancellationToken ct)
    {
        var user = HttpContext.GetVerifiedUser();

        // Check if email is already taken
        var existingUser = await userManager.FindByEmailAsync(req.NewEmail);
        if (existingUser is not null)
        {
            AddError(r => r.NewEmail, "Email address is already in use");
            await SendErrorsAsync(400, ct);
            return;
        }

        // Generate email change token and send confirmation email
        var token = await userManager.GenerateChangeEmailTokenAsync(user, req.NewEmail);

        await emailSender.SendEmailChangeConfirmationAsync(user, req.NewEmail, token);

        await SendNoContentAsync(ct);
    }
}
