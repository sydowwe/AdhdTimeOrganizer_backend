using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth.forgotPassword;

public class ForgotPasswordEndpoint(
    UserManager<User> userManager,
    IUserEmailSenderService emailSender,
    IConfiguration configuration,
    IGoogleRecaptchaService googleRecaptchaService)
    : Endpoint<ForgotPasswordRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("/auth/forgotten-password");
        AllowAnonymous();
        Throttle(hitLimit: 3, durationSeconds: 60, headerName: "X-Real-IP");
        Summary(s => { s.Summary = "Request a password reset link"; });
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
        var pageUrl = configuration["PAGE_URL"] ?? throw new InvalidOperationException("PAGE_URL not configured");
        var resetLink = $"{pageUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        await emailSender.SendPasswordResetLinkAsync(user, resetLink);

        await Send.NoContentAsync(ct);
    }
}
