namespace Sydowwe.Framework.application.dto.request.user;

/// <summary>
/// Credentials for a token-based client (browser extension, desktop, mobile).
///
/// <para>Deliberately not <see cref="PasswordLoginRequest"/>: that one also requires
/// <c>RecaptchaToken</c>, <c>StayLoggedIn</c> and <c>Timezone</c>, none of which a token client can
/// supply — there is no reCAPTCHA challenge outside a browser page, and the session length is fixed
/// by the refresh-token lifetime rather than a "remember me" box.</para>
/// </summary>
public record ExtensionLoginRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}