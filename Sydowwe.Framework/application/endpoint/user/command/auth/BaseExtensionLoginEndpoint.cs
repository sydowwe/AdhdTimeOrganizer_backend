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

/// <summary>
/// Password login for a token-based client (browser extension, desktop, mobile). The decision half —
/// password, lockout, email confirmation, the 2FA gate — is <see cref="PasswordSignInFlow"/>, shared
/// verbatim with the cookie flow in <see cref="BaseLoginEndpoint{TUser}"/>. Only the transport differs:
/// tokens come back in the body, never as cookies an extension cannot read.
///
/// <para>The extension client is a framework concept, not a host one — <see cref="IJwtService{TUser}"/>
/// already models it end to end (<c>GenerateTokensForExtensionAsync</c>,
/// <c>IssueTwoFactorPendingToken</c>, extension sessions on the refresh-token entity), so these
/// endpoints belong here rather than in each host.</para>
///
/// <para>No reCAPTCHA step, unlike the web base — a token client has no browser page to run the
/// challenge in. The throttle is the compensating control, so do not widen it.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseExtensionLoginEndpoint<TUser>(
    SignInManager<TUser> signInManager,
    UserManager<TUser> userManager,
    IJwtService<TUser> jwtService,
    IAuditService auditService,
    IOptions<TwoFactorOptions> twoFactorOptions)
    : Endpoint<ExtensionLoginRequest, ExtensionLoginResponse>
    where TUser : BaseUser
{
    protected TwoFactorOptions TwoFactor => twoFactorOptions.Value;

    /// <summary>
    /// Header the throttle buckets by — see <see cref="BaseLoginEndpoint{TUser}.ThrottleHeaderKey"/>
    /// for why a host without <see cref="TrustedIpMiddleware"/> must override this to <c>null</c>.
    /// </summary>
    protected virtual string? ThrottleHeaderKey => TrustedIpMiddleware.ClientIpHeaderName;

    /// <summary>
    /// Whether this account may sign in from an extension client. Defaults to "everyone may" —
    /// gating extension access per account is a product decision, so a host that models it (a
    /// <c>HasExtensionAccess</c> column, an entitlement, a role) overrides this. Denial is a 403 and
    /// is checked only after the password has been verified, so it cannot be used to probe which
    /// addresses are registered.
    /// </summary>
    protected virtual bool HasExtensionAccess(TUser user) => true;

    public override void Configure()
    {
        Post("/auth/extension/login");
        AllowAnonymous();
        Throttle(5, 60, ThrottleHeaderKey);
        Summary(s => { s.Summary = "Login for browser extension clients"; });
    }

    public override async Task HandleAsync(ExtensionLoginRequest req, CancellationToken ct)
    {
        // PII: this request carries an email and a password. Do not add logging here.
        var signIn = await PasswordSignInFlow.RunAsync(
            signInManager, userManager, auditService, TwoFactor, req.Email, req.Password, ct);

        if (signIn.Failed)
        {
            AddError(signIn.ErrorMessage!);
            await Send.ErrorsAsync(signIn.StatusCode, ct);
            return;
        }

        var user = signIn.User!;

        if (!HasExtensionAccess(user))
        {
            AddError("Extension access not enabled for this account");
            await Send.ErrorsAsync(403, ct);
            return;
        }

        if (signIn.Outcome is PasswordSignInOutcome.TwoFactorRequired)
        {
            // Same partial-auth token as the web flow, just returned in the body rather than set as a
            // cookie — no session is issued until /auth/login/2fa/extension validates the code.
            await Send.OkAsync(new ExtensionLoginResponse
            {
                RequiresTwoFactor = true,
                RequiresTwoFactorSetup = signIn.RequiresTwoFactorSetup,
                PendingAuthToken = jwtService.IssueTwoFactorPendingToken(user.Id, signIn.RequiresTwoFactorSetup)
            }, ct);
            return;
        }

        await auditService.LogAsync("ExtensionLoginSuccess", entityName: typeof(TUser).Name, entityId: user.Id, ct: ct);

        var (accessToken, refreshToken) = await jwtService.GenerateTokensForExtensionAsync(AuthMethodEnum.Password, user);

        await Send.OkAsync(new ExtensionLoginResponse
        {
            RequiresTwoFactor = false,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        }, ct);
    }
}