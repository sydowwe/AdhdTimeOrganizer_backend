using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;

namespace AdhdTimeOrganizer.application.endpoint.user.command;

public class ForgotPasswordEndpoint(UserManager<User> userManager, IUserEmailSenderService emailSender, IConfiguration configuration)
    : Endpoint<ForgotPasswordRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("user/forgot-password");
        AllowAnonymous();
        Throttle(hitLimit: 3, durationSeconds: 60, headerName: "X-Client-Id");
        Summary(s => { s.Summary = "Request a password reset link"; });
    }

    public override async Task HandleAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(req.Email);
        
        // Always return success to prevent user enumeration
        // Don't reveal if email exists or not
        if (user is null || !user.EmailConfirmed)
        {
            await SendNoContentAsync(ct);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var pageUrl = configuration["PAGE_URL"] ?? throw new InvalidOperationException("PAGE_URL not configured");
        var resetLink = $"{pageUrl}/reset-password?userId={user.Id}&token={Uri.EscapeDataString(token)}";
        
        await emailSender.SendPasswordResetLinkAsync(user, resetLink);
        
        await SendNoContentAsync(ct);
    }
}
