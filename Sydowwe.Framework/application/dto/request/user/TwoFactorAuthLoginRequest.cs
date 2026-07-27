namespace Sydowwe.Framework.application.dto.request.user;

public record TwoFactorAuthLoginRequest : TwoFactorAuthRequest
{
    public bool StayLoggedIn { get; set; }

    /// <summary>
    /// The partial-auth token from the password step, for clients that cannot use cookies (browser
    /// extension, desktop, mobile). Browser clients leave this null — the same token arrives in the
    /// <c>two-factor-token</c> cookie instead. Which one an endpoint reads is decided by its
    /// <c>ReadPendingToken</c> override, never by the client, so a cookie endpoint cannot be talked
    /// into accepting a body token.
    /// </summary>
    public string? PendingAuthToken { get; set; }
}