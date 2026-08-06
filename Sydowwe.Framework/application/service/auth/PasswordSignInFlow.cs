using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.config;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.application.service.auth;

/// <summary>How the password step ended. Everything except <see cref="Authenticated"/> and
/// <see cref="TwoFactorRequired"/> is a terminal failure the caller renders as an error response.</summary>
public enum PasswordSignInOutcome
{
    /// <summary>No such user, or the password was wrong. Deliberately indistinguishable.</summary>
    InvalidCredentials,

    /// <summary>Identity lockout is active. The message carries the remaining time.</summary>
    LockedOut,

    /// <summary>Password was correct but the address was never confirmed.</summary>
    EmailNotConfirmed,

    /// <summary>Password accepted; the caller must hand back a partial-auth token, never a session.</summary>
    TwoFactorRequired,

    /// <summary>Password accepted and no second factor is due — issue the real session.</summary>
    Authenticated
}

public sealed record PasswordSignInResult<TUser>(
    PasswordSignInOutcome Outcome,
    TUser? User,
    string? ErrorMessage = null,
    bool RequiresTwoFactorSetup = false)
    where TUser : BaseUser
{
    public bool Failed => Outcome is PasswordSignInOutcome.InvalidCredentials
        or PasswordSignInOutcome.LockedOut
        or PasswordSignInOutcome.EmailNotConfirmed;

    /// <summary>
    /// HTTP status for a failed outcome. Load-bearing and identical across transports: 401 for bad
    /// credentials, 403 for lockout and for an unconfirmed address.
    /// </summary>
    public int StatusCode => Outcome switch
    {
        PasswordSignInOutcome.InvalidCredentials => 401,
        PasswordSignInOutcome.LockedOut => 403,
        PasswordSignInOutcome.EmailNotConfirmed => 403,
        _ => 200
    };
}

/// <summary>
/// The password / lockout / email-confirmation / 2FA decision, shared by every password login
/// transport — the cookie flow (<c>BaseLoginEndpoint</c>) and the token flow
/// (<c>BaseExtensionLoginEndpoint</c>). Only the transport differs between them, so this must not be
/// re-implemented per client: a second copy is how the two drift apart on lockout or on the 2FA gate.
///
/// <para>Deliberately a plain static helper rather than a DI service — every caller already injects
/// all four dependencies, and an open generic over <c>TUser</c> would need hand-registration per host.</para>
/// </summary>
public static class PasswordSignInFlow
{
    public static async Task<PasswordSignInResult<TUser>> RunAsync<TUser>(
        SignInManager<TUser> signInManager,
        UserManager<TUser> userManager,
        IAuditService auditService,
        TwoFactorOptions twoFactorOptions,
        string email,
        string password,
        CancellationToken ct = default)
        where TUser : BaseUser
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Generic error to prevent user enumeration
            return new PasswordSignInResult<TUser>(PasswordSignInOutcome.InvalidCredentials, null,
                "Invalid email or password");
        }

        // CheckPasswordSignInAsync verifies the password and feeds Identity lockout. We do NOT use
        // PasswordSignInAsync / the Identity TwoFactorUserIdScheme partial cookie: this deployment is
        // AddIdentityCore + JwtBearer only (no Identity cookie schemes are registered), so the 2FA
        // partial state is carried by our own short-lived token instead.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, true);

        if (result.IsLockedOut)
        {
            var lockoutDuration = user.LockoutEnd!.Value - DateTimeOffset.UtcNow;
            var minutes = (int)lockoutDuration.TotalMinutes;
            var seconds = lockoutDuration.Seconds;
            await auditService.LogAndSaveAsync("LoginLockedOut", entityName: typeof(TUser).Name, entityId: user.Id, ct: ct);
            return new PasswordSignInResult<TUser>(PasswordSignInOutcome.LockedOut, user,
                $"Too many failed login attempts. Try again in {minutes}m {seconds}s");
        }

        if (!result.Succeeded)
        {
            // Generic error to prevent user enumeration
            await auditService.LogAndSaveAsync("LoginFailed", entityName: typeof(TUser).Name, entityId: user.Id, ct: ct);
            return new PasswordSignInResult<TUser>(PasswordSignInOutcome.InvalidCredentials, user,
                "Invalid email or password");
        }

        // Checked after password verification so the answer can't be used to probe which addresses
        // are registered.
        if (!user.EmailConfirmed)
        {
            return new PasswordSignInResult<TUser>(PasswordSignInOutcome.EmailNotConfirmed, user, "Email not confirmed");
        }

        // Mode.Required makes 2FA mandatory regardless of the per-user flag; Mode.Disabled skips the
        // step even for a user whose flag is set, so a stale flag can't strand someone in a
        // deployment that has no 2FA endpoints mapped.
        var twoFactorRequired = twoFactorOptions.Mode switch
        {
            TwoFactorMode.Disabled => false,
            TwoFactorMode.Required => true,
            _ => user.TwoFactorEnabled
        };

        if (twoFactorRequired)
        {
            // Enabled-but-unconfigured (no authenticator key yet) means the user must set up an
            // authenticator before they can produce a code — the normal state of every account under
            // Mode.Required. Either way the caller hands back only a partial-auth token, never a session.
            var requiresSetup = await userManager.GetAuthenticatorKeyAsync(user) is null;
            await auditService.LogAndSaveAsync("LoginPending2FA", entityName: typeof(TUser).Name, entityId: user.Id, ct: ct);
            return new PasswordSignInResult<TUser>(PasswordSignInOutcome.TwoFactorRequired, user,
                RequiresTwoFactorSetup: requiresSetup);
        }

        return new PasswordSignInResult<TUser>(PasswordSignInOutcome.Authenticated, user);
    }
}