using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

public abstract class BaseValidateTwoFactorAuthForLoginEndpoint<TUser>(
    UserManager<TUser> userManager,
    ITwoFactorAuthService<TUser> twoFactorAuthService,
    IJwtService<TUser> jwtService,
    IAuditService auditService)
    : Endpoint<TwoFactorAuthLoginRequest>
    where TUser : BaseUser
{
    /// <summary>See <c>BaseLoginEndpoint.ThrottleHeaderKey</c>; override to null without TrustedIpMiddleware.</summary>
    protected virtual string? ThrottleHeaderKey => TrustedIpMiddleware.ClientIpHeaderName;

    public override void Configure()
    {
        // Sits under the login route because that is what it is: the second step of /auth/login.
        Post("auth/login/2fa");
        AllowAnonymous();
        Throttle(5, 60, ThrottleHeaderKey);
        Summary(s => { s.Summary = "Validate 2FA for login"; });
    }

    public override async Task HandleAsync(TwoFactorAuthLoginRequest request, CancellationToken ct)
    {
        // The user is identified by the short-lived partial-auth token from the password step, not by
        // an app session — they are not authenticated for anything else yet.
        var pendingToken = ReadPendingToken(request);
        if (string.IsNullOrEmpty(pendingToken))
        {
            AddError("Two-factor session expired. Please sign in again.");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        // Single validation path for every transport: single-use guard, lockout, TOTP or recovery code.
        var result = await twoFactorAuthService.ValidatePendingLoginToken(pendingToken, request.Token, ct);
        if (result.Failed)
        {
            await auditService.LogAndSaveAsync("TwoFactorFailed", ct: ct);
            AddError(result.ErrorMessage ?? "Invalid 2FA token");
            await Send.ErrorsAsync(401, ct);
            return;
        }

        var user = result.Data;
        jwtService.ClearTwoFactorPendingCookie(HttpContext);
        await auditService.LogAsync("TwoFactorSuccess", entityName: typeof(TUser).Name, entityId: user.Id, ct: ct);
        await OnAuthenticatedAsync(user, request, ct);
    }

    /// <summary>
    /// Where the partial-auth token comes from. Cookie by default; a token-based client overrides
    /// this to read it out of the request body.
    /// </summary>
    protected virtual string? ReadPendingToken(TwoFactorAuthLoginRequest request)
    {
        return HttpContext.Request.Cookies.TryGetValue(AuthCookies.TwoFactorTokenName, out var token) ? token : null;
    }

    /// <summary>
    /// Issues the real session once the second factor checks out. Cookies by default; override to
    /// return a token pair in the body instead.
    /// </summary>
    protected virtual async Task OnAuthenticatedAsync(TUser user, TwoFactorAuthLoginRequest request, CancellationToken ct)
    {
        await jwtService.GenerateJwtAndSetAuthCookie(request.StayLoggedIn, AuthMethodEnum.Password, user, HttpContext);
        await Send.NoContentAsync(ct);
    }
}