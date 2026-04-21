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
        if (user == null)
        {
            await Send.ResponseAsync("User not found", 404, ct);
            return;
        }

        if (await userManager.IsEmailConfirmedAsync(user))
        {
            await Send.ResponseAsync("Email is already confirmed", 422, ct);
            return;
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        
        await emailSender.SendConfirmationLinkAsync(user, token);
        
        await Send.OkAsync("Confirmation email has been resent successfully", ct);
    }
}
