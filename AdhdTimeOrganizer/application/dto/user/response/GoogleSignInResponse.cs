using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.application.dto.response.user;

public record GoogleSignInResponse : EmailResponse
{
    public AvailableLocales CurrentLocale { get; init; }

    /// <summary>
    /// Normally false — a federated sign-in is taken to satisfy 2FA. Only set when the deployment
    /// turns off <c>TwoFactor:FederatedLoginSatisfiesTwoFactor</c>, in which case no session has been
    /// issued and the client must complete <c>/auth/login/2fa</c> just as after a password login.
    /// </summary>
    public bool RequiresTwoFactor { get; init; }

    public bool RequiresTwoFactorSetup { get; init; }
}