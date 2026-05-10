using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.emailConfirmation;

public class ResendConfirmationEmailEndpoint(IUserEmailSenderService emailSender,UserManager<User> userManager) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("auth/resend-confirmation-email/{userId:required:long}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Route<long>("userId");

        if (userId <= 0)
        {
            await Send.ResponseAsync("UserId must be supplied", 400, ct);
            return;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());

        // Return the same response whether the user exists or is already confirmed to prevent enumeration
        if (user == null || await userManager.IsEmailConfirmedAsync(user))
        {
            await Send.OkAsync("Confirmation email sent if applicable", ct);
            return;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        await emailSender.SendConfirmationLinkAsync(user, token);

        await Send.OkAsync("Confirmation email sent if applicable", ct);
    }
}
