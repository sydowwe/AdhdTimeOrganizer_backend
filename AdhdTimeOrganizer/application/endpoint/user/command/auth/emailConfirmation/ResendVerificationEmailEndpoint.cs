using AdhdTimeOrganizer.application.extensions;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.emailConfirmation;

public class ResendVerificationEmailEndpoint(
    IUserEmailSenderService emailSender,
    UserManager<User> userManager,
    IDistributedCache cache) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("user/resend-verification");
        Summary(s => { s.Summary = "Resend verification email to the authenticated user (max 1/min)"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetId();

        if (await userManager.IsEmailConfirmedAsync(await userManager.FindByIdAsync(userId.ToString()) ?? throw new InvalidOperationException()))
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var throttleKey = $"throttle:resend-verification:{userId}";
        if (await cache.GetStringAsync(throttleKey, ct) is not null)
        {
            AddError("Please wait 1 minute before requesting another verification email.");
            await Send.ErrorsAsync(429, ct);
            return;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            await Send.UnauthorizedAsync(ct);
            return;
        }

        await cache.SetStringAsync(throttleKey, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1)
        }, ct);

        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await emailSender.SendConfirmationLinkAsync(user, token);

        await Send.NoContentAsync(ct);
    }
}
