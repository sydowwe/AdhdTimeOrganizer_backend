using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Re-sends the confirmation link for an unconfirmed address.
///
/// <para>Two throttles, deliberately: the per-IP <c>Throttle</c> caps one caller, the per-email
/// distributed-cache key caps one mailbox no matter how many IPs ask for it.</para>
/// </summary>
public abstract class BaseResendConfirmationEmailEndpoint<TUser>(
    IUserEmailSenderService<TUser> emailSender,
    UserManager<TUser> userManager,
    IDistributedCache cache)
    : Endpoint<EmailRequest, EmptyResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/auth/resend-confirmation-email");
        AllowAnonymous();
        Throttle(3, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s => { s.Summary = "Resend email confirmation link to user"; });
    }

    public override async Task HandleAsync(EmailRequest req, CancellationToken ct)
    {
        // Cache key only — the address must never reach a log line.
        var throttleKey = $"throttle:resend-confirmation:{req.Email.ToLowerInvariant()}";
        if (await cache.GetStringAsync(throttleKey, ct) is not null)
        {
            AddError("Please wait 1 minute before requesting another confirmation email.");
            await Send.ErrorsAsync(429, ct);
            return;
        }

        var user = await userManager.FindByEmailAsync(req.Email);

        // 204 whether or not the address is registered or already confirmed — this is anti-enumeration,
        // not a missing error path. Answering 404/400 here would turn the route into an oracle for
        // which addresses hold an account. Do not "improve" it into a real error.
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