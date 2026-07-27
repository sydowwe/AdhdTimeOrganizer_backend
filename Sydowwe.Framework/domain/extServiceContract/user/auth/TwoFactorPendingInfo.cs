namespace Sydowwe.Framework.domain.extServiceContract.user.auth;

/// <summary>
/// Decoded contents of the partial-auth ("2FA pending") token issued after a successful password
/// step. <see cref="RequiresSetup"/> is <c>true</c> when the user has 2FA enabled but has not yet
/// provisioned an authenticator key (first-login forced setup).
/// </summary>
/// <param name="TokenId">
/// The token's <c>jti</c>. The token is stateless, so this is what the single-use guard in
/// <c>ValidatePendingLoginToken</c> keys on — marking the id consumed is what stops one password
/// step from funding unlimited 2FA guesses.
/// </param>
public record TwoFactorPendingInfo(long UserId, bool RequiresSetup, string TokenId);