using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// First-login 2FA provisioning. Reachable only with the partial-auth cookie issued by the password
/// step when <c>RequiresTwoFactorSetup</c> is true (2FA enabled but no authenticator key yet, e.g. a
/// freshly provisioned employee). Returns the QR + recovery codes; the user then confirms a code via
/// <see cref="BaseValidateTwoFactorAuthForLoginEndpoint{TUser}"/> to obtain a real session.
/// </summary>
public abstract class BaseSetupTwoFactorForLoginEndpoint<TUser>(
    UserManager<TUser> userManager,
    ITwoFactorAuthService<TUser> twoFactorAuthService,
    IJwtService<TUser> jwtService,
    IAuditService auditService)
    : EndpointWithoutRequest<TwoFactorAuthResponse>
    where TUser : BaseUser
{
    /// <summary>See <c>BaseLoginEndpoint.ThrottleHeaderKey</c>; override to null without TrustedIpMiddleware.</summary>
    protected virtual string? ThrottleHeaderKey => TrustedIpMiddleware.ClientIpHeaderName;

    public override void Configure()
    {
        Post("auth/login/2fa/setup");
        AllowAnonymous();
        Throttle(10, 60, ThrottleHeaderKey);
        Summary(s => { s.Summary = "Provision 2FA during first login"; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var pending = jwtService.ReadTwoFactorPendingCookie(HttpContext);
        if (pending is null)
        {
            AddError("Two-factor session expired. Please sign in again.");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var user = await userManager.FindByIdAsync(pending.UserId.ToString());
        if (user is null)
        {
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var result = await twoFactorAuthService.SetUpTwoFactorAuth(user);
        if (result.Failed)
        {
            AddError(result.ErrorMessage ?? "Failed to set up two-factor authentication.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        await auditService.LogAndSaveAsync("TwoFactorSetupInitiated", entityName: typeof(TUser).Name, entityId: user.Id, ct: ct);
        await Send.OkAsync(result.Data, ct);
    }
}