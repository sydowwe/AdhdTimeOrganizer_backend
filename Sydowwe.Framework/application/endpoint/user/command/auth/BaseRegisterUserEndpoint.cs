using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.application.service.auth;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.application.endpoint.user.command.auth;

/// <summary>
/// Signs a new user up with an email and a password: captcha → duplicate-email check → the shared
/// <see cref="UserRegistrationFlow"/> (account, role, 2FA provisioning, host defaults — one
/// transaction) → confirmation email.
///
/// <para>The confirmation email is sent <b>after</b> the commit and outside the transaction: it is a
/// side effect that cannot be rolled back, so it must not go out for an account that ends up not
/// existing. It is skipped for an already-confirmed address (a host that confirms out of band).</para>
///
/// <para>Injects <c>DbContext</c> rather than a concrete context so the flow's transaction stays
/// host-agnostic; hosts already alias their context (<c>AddScoped&lt;DbContext&gt;</c>) for the same
/// reason the modules need it.</para>
///
/// <para>PII: this endpoint handles an email and a password. It logs nothing, deliberately — see
/// CLAUDE.md "Logging (no PII at the call site)". Do not add logging here.</para>
///
/// <para>Abstract like every other endpoint in this assembly: the Framework assembly is deliberately
/// excluded from FastEndpoints discovery (<c>o.Assemblies</c> in the host's <c>Program.cs</c>), so a
/// concrete endpoint here would never be routed. Hosts derive a thin subclass instead.</para>
/// </summary>
public abstract class BaseRegisterUserEndpoint<TUser, TRequest>(
    UserManager<TUser> userManager,
    DbContext dbContext,
    IGoogleRecaptchaService googleRecaptchaService,
    ITwoFactorAuthService<TUser> twoFactorAuthService,
    IUserEmailSenderService<TUser> emailSender,
    IUserDefaultsService userDefaultsService)
    : Endpoint<TRequest, TwoFactorAuthResponse>
    where TUser : BaseUser
    where TRequest : BasePasswordRegistrationRequest<TUser>
{
    public override void Configure()
    {
        Post("/auth/register");
        AllowAnonymous();
        Throttle(5, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s => s.Summary = "Register a user with email & password");
    }

    /// <summary>
    /// Host-specific follow-up for a brand-new account, run <b>inside</b> the registration
    /// transaction — a failed <see cref="Result"/> rolls the whole sign-up back. The per-user rows a
    /// new account needs are not this hook's job: those come from <see cref="IUserDefaultsService"/>,
    /// which the flow calls for you. Use this for anything else that must be atomic with the sign-up.
    /// </summary>
    protected virtual Task<Result> AfterUserCreatedAsync(TUser user, CancellationToken ct) =>
        Task.FromResult(Result.Successful());

    public override async Task HandleAsync(TRequest req, CancellationToken ct)
    {
        var recaptchaResult = await googleRecaptchaService.VerifyRecaptchaAsync(req.RecaptchaToken, "register");
        if (recaptchaResult.Failed)
        {
            AddError("Recaptcha failed.");
            await Send.ErrorsAsync(403, ct);
            return;
        }

        var existing = await userManager.FindByEmailAsync(req.Email);
        if (existing is not null)
        {
            AddError("User already exists with this email.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var newUser = req.ToEntity;

        // Provisioned inside the transaction: a secret handed to the user for an account that then
        // fails to register would be a secret for an account that does not exist.
        TwoFactorAuthResponse? twoFaData = null;

        var result = await UserRegistrationFlow.RunAsync(
            userManager, dbContext, userDefaultsService, newUser, req.Password,
            async (user, token) =>
            {
                var twoFaResult = await twoFactorAuthService.SetUpTwoFactorAuth(user);
                if (twoFaResult.Failed)
                    return twoFaResult;

                twoFaData = twoFaResult.Data;
                return await AfterUserCreatedAsync(user, token);
            }, ct);

        if (result.Failed)
        {
            AddError(result.ErrorMessage!);
            await Send.ErrorsAsync(result.StatusCode, ct);
            return;
        }

        if (!newUser.EmailConfirmed)
        {
            var token = await userManager.GenerateEmailConfirmationTokenAsync(newUser);
            await emailSender.SendConfirmationLinkAsync(newUser, token);
        }

        await Send.OkAsync(twoFaData!, ct);
    }
}
