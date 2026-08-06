using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Consumes a password-reset token and sets the new password.
///
/// <para>Unlike <see cref="BaseForgotPasswordEndpoint{TUser}"/> this reports failures as 400 —
/// the caller already holds a reset token, so an error response reveals nothing an attacker
/// couldn't already infer. An unknown user id is reported as "invalid or expired reset token"
/// so the two cases stay indistinguishable.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseResetPasswordEndpoint<TUser>(
    UserManager<TUser> userManager,
    IRefreshTokenService refreshTokenService,
    IGoogleRecaptchaService googleRecaptchaService)
    : Endpoint<ResetPasswordRequest, EmptyResponse>
    where TUser : BaseUser
{
    public override void Configure()
    {
        Post("/auth/reset-password");
        AllowAnonymous();
        Throttle(5, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s => { s.Summary = "Reset password using reset token"; });
    }

    public override async Task HandleAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "reset_password");
        if (recaptchaResult.Failed)
        {
            AddError("Recaptcha verification failed.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var user = await userManager.FindByIdAsync(req.UserId.ToString());
        if (user is null)
        {
            AddError("Invalid or expired reset token");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var result = await userManager.ResetPasswordAsync(user, req.Token, req.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                AddError(error.Description);
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Credential-reset event: no session may outlive the change.
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        await refreshTokenService.RevokeAllUserTokensAsync(user.Id, ipAddress);

        await Send.NoContentAsync(ct);
    }
}