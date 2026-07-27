using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.application.service.auth;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

public abstract class BaseLoginEndpoint<TUser>(
    SignInManager<TUser> signInManager,
    UserManager<TUser> userManager,
    IJwtService<TUser> jwtService,
    IGoogleRecaptchaService googleRecaptchaService,
    IAuditService auditService,
    IOptions<TwoFactorOptions> twoFactorOptions)
    : Endpoint<PasswordLoginRequest, LoginResponse>
    where TUser : BaseUser
{
    protected TwoFactorOptions TwoFactor => twoFactorOptions.Value;

    /// <summary>
    /// Header the throttle buckets by. Defaults to <see cref="TrustedIpMiddleware.ClientIpHeaderName"/>,
    /// which <see cref="TrustedIpMiddleware"/> overwrites from the connection — so the key is
    /// non-spoofable and login attempts are limited per client rather than globally.
    /// <para><b>A host that does not register that middleware must override this to <c>null</c></b>
    /// (bucket by remote IP). Left pointing at a header nothing sets, every caller shares one bucket
    /// and the limit becomes global: five failed logins anywhere lock out the whole world for a minute.</para>
    /// </summary>
    protected virtual string? ThrottleHeaderKey => TrustedIpMiddleware.ClientIpHeaderName;

    public override void Configure()
    {
        Post("auth/login");
        AllowAnonymous();
        Throttle(5, 60, ThrottleHeaderKey);
        Summary(s => { s.Summary = "Login a user"; });
    }

    public override async Task HandleAsync(PasswordLoginRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "login");
        if (recaptchaResult.Failed)
        {
            AddError("Recaptcha verification failed.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        // Password, lockout, email confirmation and the 2FA gate are shared with the token-based
        // extension login (BaseExtensionLoginEndpoint) — only the transport below differs.
        var signIn = await PasswordSignInFlow.RunAsync(
            signInManager, userManager, auditService, TwoFactor, req.Email, req.Password, ct);

        if (signIn.Failed)
        {
            AddError(signIn.ErrorMessage!);
            await Send.ErrorsAsync(signIn.StatusCode, ct);
            return;
        }

        var user = signIn.User!;

        if (signIn.Outcome is PasswordSignInOutcome.TwoFactorRequired)
        {
            await IssuePendingTokenAsync(user, signIn.RequiresTwoFactorSetup, ct);
            await Send.OkAsync(
                new LoginResponse { RequiresTwoFactor = true, RequiresTwoFactorSetup = signIn.RequiresTwoFactorSetup }, ct);
            return;
        }

        await auditService.LogAsync("LoginSuccess", entityName: typeof(TUser).Name, entityId: user.Id, ct: ct);
        await jwtService.GenerateJwtAndSetAuthCookie(req.StayLoggedIn, AuthMethodEnum.Password, user, HttpContext);
        await Send.OkAsync(new LoginResponse { RequiresTwoFactor = false }, ct);
    }

    /// <summary>
    /// Hands the partial-auth token to the caller. Defaults to the browser cookie; a token-based
    /// client (extension, desktop, mobile) overrides this to return
    /// <see cref="IJwtService.IssueTwoFactorPendingToken"/> in the response body instead.
    /// </summary>
    protected virtual Task IssuePendingTokenAsync(TUser user, bool requiresSetup, CancellationToken ct)
    {
        jwtService.IssueTwoFactorPendingCookie(HttpContext, user.Id, requiresSetup);
        return Task.CompletedTask;
    }
}