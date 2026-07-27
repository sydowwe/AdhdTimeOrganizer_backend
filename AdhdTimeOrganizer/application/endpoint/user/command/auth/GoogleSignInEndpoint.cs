using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.application.middleware;
using Sydowwe.Framework.application.service.auth;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.domain.serviceContract;

namespace AdhdTimeOrganizer.application.endpoint.user.command.auth;

public class GoogleSignInEndpoint(
    UserManager<User> userManager,
    AppDbContext dbContext,
    IJwtService<User> jwtService,
    IUserDefaultsService userDefaultsService,
    IGoogleSignInService googleSignInService,
    IOptions<TwoFactorOptions> twoFactorOptions)
    : Endpoint<GoogleSignInRequest, GoogleSignInResponse>
{
    public override void Configure()
    {
        Post("/auth/login/google");
        AllowAnonymous();
        Throttle(10, 60, TrustedIpMiddleware.ClientIpHeaderName);
        Summary(s => { s.Summary = "Sign in with Google OAuth"; });
    }

    public override async Task HandleAsync(GoogleSignInRequest req, CancellationToken ct)
    {
        var googleSignInResult = await googleSignInService.GetUserInfoFromGoogleSignInCode(req.Code);
        if (googleSignInResult.Failed)
        {
            AddError("Failed to verify Google sign-in code.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        var googleInfo = googleSignInResult.Data;
        var googleUserId = googleInfo.UserId;

        var user = await userManager.FindByEmailAsync(googleInfo.Email);
        if (user is null)
        {
            var registrationRequest = new GoogleAuthRegistrationRequest
            {
                Email = googleInfo.Email,
                Timezone = req.Timezone,
                TwoFactorEnabled = false,
                CurrentLocale = AvailableLocales.En,
                GoogleOAuthUserId = googleUserId
            };

            user = await Register(registrationRequest, ct);
            if (user is null)
                return;
        }
        else if (!user.HasGoogleOAuth || user.GoogleOAuthUserId != googleUserId)
        {
            AddError("Could not sign in with Google.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        // Federated sign-in and the local second factor: by default Google's authentication is taken
        // to satisfy 2FA (the usual convention — the IdP authenticated the user, often with its own
        // MFA). Deployments that want the local factor regardless set
        // TwoFactor:FederatedLoginSatisfiesTwoFactor to false, and then a 2FA user must complete the
        // code step here exactly as on password login. Stated as policy rather than left implicit,
        // because the default does mean a compromised Google account reaches this account.
        var twoFactor = twoFactorOptions.Value;
        var mustVerifySecondFactor = twoFactor.Mode is not TwoFactorMode.Disabled &&
                                     !twoFactor.FederatedLoginSatisfiesTwoFactor &&
                                     (user.TwoFactorEnabled || twoFactor.Mode is TwoFactorMode.Required);

        if (mustVerifySecondFactor)
        {
            var requiresSetup = await userManager.GetAuthenticatorKeyAsync(user) is null;
            jwtService.IssueTwoFactorPendingCookie(HttpContext, user.Id, requiresSetup);

            await Send.OkAsync(new GoogleSignInResponse
            {
                Email = googleInfo.Email,
                CurrentLocale = user.Locale,
                RequiresTwoFactor = true,
                RequiresTwoFactorSetup = requiresSetup
            }, ct);
            return;
        }

        await jwtService.GenerateJwtAndSetAuthCookie(
            true, AuthMethodEnum.Google, user, HttpContext);

        var response = new GoogleSignInResponse
        {
            Email = googleInfo.Email,
            CurrentLocale = user.Locale
        };

        await Send.OkAsync(response, ct);
    }

    private async Task<User?> Register(GoogleAuthRegistrationRequest req, CancellationToken ct)
    {
        var newUser = req.ToEntity;

        newUser.GoogleOAuthUserId = req.GoogleOAuthUserId;
        // Google vouched for the address; there is nothing left for a confirmation mail to prove.
        newUser.EmailConfirmed = true;

        // No password: the Google account is the credential. Everything else — role, defaults,
        // one transaction — is the same as a password sign-up, so it comes from the shared flow.
        var result = await UserRegistrationFlow.RunAsync(
            userManager, dbContext, userDefaultsService, newUser, ct: ct);

        if (result.Failed)
        {
            // Duplicate is reworded: on this route it means the address exists but is not linked to
            // this Google account, and "user already exists" would confirm the address to a prober.
            AddError(result.Outcome is UserRegistrationOutcome.DuplicateUser
                ? "Could not sign in with Google."
                : result.ErrorMessage!);
            await Send.ErrorsAsync(result.StatusCode, ct);
            return null;
        }

        return result.User;
    }
}