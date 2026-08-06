using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Mails a password-reset link to the address in the request.
///
/// <para>Every path answers 204 — unknown address, unconfirmed address, and even a failed reCAPTCHA.
/// Anything else would turn this route into a user-enumeration oracle. Note the deliberate asymmetry
/// with <see cref="BaseResetPasswordEndpoint{TUser}"/>, which *does* return 400 on reCAPTCHA failure:
/// there the caller already holds a reset token, so there is nothing left to leak.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseForgotPasswordEndpoint<TUser>(
    UserManager<TUser> userManager,
    IUserEmailSenderService<TUser> emailSender,
    IConfiguration configuration,
    IGoogleRecaptchaService googleRecaptchaService)
    : Endpoint<ForgotPasswordRequest, EmptyResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/auth/forgotten-password");
        AllowAnonymous();
        Throttle(3, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s => { s.Summary = "Request a password reset link"; });
    }

    /// <summary>
    /// Builds the link mailed to the user. The path is a SPA route, i.e. a product decision — a
    /// different frontend on this framework serves the reset form from somewhere else — so hosts
    /// override this rather than reshaping the flow.
    /// </summary>
    protected virtual string BuildResetLink(TUser user, string token)
    {
        var pageUrl = configuration["PAGE_URL"] ?? throw new InvalidOperationException("PAGE_URL not configured");
        return $"{pageUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";
    }

    public override async Task HandleAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "forgot_password");
        if (recaptchaResult.Failed)
        {
            // Same no-content response — don't reveal reCAPTCHA failure
            await Send.NoContentAsync(ct);
            return;
        }

        var user = await userManager.FindByEmailAsync(req.Email);

        // Always return success to prevent user enumeration
        if (user is null || !user.EmailConfirmed)
        {
            await Send.NoContentAsync(ct);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetLink = BuildResetLink(user, token);

        await emailSender.SendPasswordResetLinkAsync(user, user.Email!, resetLink);

        await Send.NoContentAsync(ct);
    }
}