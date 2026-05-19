using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.emailConfirmation;

public class ResendConfirmationEmailEndpoint(
    IUserEmailSenderService emailSender,
    UserManager<User> userManager,
    IDistributedCache cache)
    : Endpoint<EmailRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("/auth/resend-confirmation-email");
        AllowAnonymous();
        Throttle(hitLimit: 3, durationSeconds: 60, headerName: "X-Real-IP");
        Summary(s => { s.Summary = "Resend email confirmation link to user"; });
    }

    public override async Task HandleAsync(EmailRequest req, CancellationToken ct)
    {
        var throttleKey = $"throttle:resend-confirmation:{req.Email.ToLowerInvariant()}";
        if (await cache.GetStringAsync(throttleKey, ct) is not null)
        {
            AddError("Please wait 1 minute before requesting another confirmation email.");
            await Send.ErrorsAsync(429, ct);
            return;
        }

        var user = await userManager.FindByEmailAsync(req.Email);

        // Always return 204 to prevent user enumeration
        if (user == null || await userManager.IsEmailConfirmedAsync(user))
        {
            await Send.NoContentAsync(ct);
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
