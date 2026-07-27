namespace Sydowwe.Framework.application.dto.response.user;

// Inherits EmailResponse so the login reply always echoes the address that was authenticated — the
// SPA shows it on the 2FA screen, where no session exists yet to look it up.
public record LoginResponse
{
    public required bool RequiresTwoFactor { get; init; }

    /// <summary>
    /// True when 2FA is required but the user has not yet provisioned an authenticator (first-login
    /// forced setup). The client must send the user through the 2FA setup screen (scan QR, save
    /// recovery codes) before they can supply a code. Only meaningful when
    /// <see cref="RequiresTwoFactor"/> is true.
    /// </summary>
    public bool RequiresTwoFactorSetup { get; init; }
}