namespace Sydowwe.Framework.config;

public enum TwoFactorMode
{
    /// <summary>
    /// Login never branches to a 2FA step, even for an account whose <c>TwoFactorEnabled</c> flag is
    /// set. Use in deployments that don't offer 2FA at all — it also stops a stale flag on an old row
    /// from locking someone out of a host that has no 2FA endpoints mapped.
    /// </summary>
    Disabled,

    /// <summary>Users opt in from their own settings. 2FA is enforced at login for those who did.</summary>
    Optional,

    /// <summary>
    /// Every account must have 2FA. Accounts can therefore exist with 2FA required but no
    /// authenticator provisioned, which is what <c>LoginResponse.RequiresTwoFactorSetup</c> and the
    /// setup endpoint are for — without them such a user could never complete a login.
    /// </summary>
    Required
}

/// <summary>
/// Deployment policy for two-factor authentication. Bind from the <c>TwoFactor</c> configuration
/// section; the defaults (<see cref="TwoFactorMode.Optional"/>, federated logins accepted) match how
/// a self-service portal normally behaves.
/// <para>Note the endpoints are a separate axis: Framework's 2FA endpoints are abstract, so a host
/// that never subclasses them simply has no 2FA routes. This option controls whether the *login*
/// flow branches; not mapping the endpoints controls whether the branch has anywhere to go. A host
/// running <see cref="TwoFactorMode.Disabled"/> normally does neither.</para>
/// </summary>
public class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    public TwoFactorMode Mode { get; set; } = TwoFactorMode.Optional;

    /// <summary>
    /// Whether signing in through an external identity provider (Google, Microsoft, …) satisfies the
    /// 2FA requirement, skipping the code prompt.
    ///
    /// <para>Default <c>true</c>, which is the common convention: the IdP already authenticated the
    /// user, and a provider account with its own MFA is usually a stronger factor than a TOTP app.
    /// The trade-off is explicit rather than accidental — with this on, compromise of the linked
    /// provider account is enough to reach the account here, and your 2FA will not stop it. Set
    /// <c>false</c> to demand the local second factor even after a federated sign-in.</para>
    ///
    /// <para>A stricter implementation would inspect the provider's <c>amr</c>/<c>acr</c> claims and
    /// skip only when the IdP actually performed MFA. This flag is the coarse version of that.</para>
    /// </summary>
    public bool FederatedLoginSatisfiesTwoFactor { get; set; } = true;
}