using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.domain.extServiceContract.user.auth;

public record RefreshTokenSessionData(
    long Id,
    string TokenHash,
    string? UserAgent,
    string? IpAddress,
    DateTime CreatedAt
);

/// <summary>
/// Store for the opaque refresh tokens backing a session. Only hashes are persisted, never the raw
/// token.
/// <para>The <c>ipAddress</c> defaults mirror the implementation exactly — C# binds default values at
/// the call site from the *static* type, so an interface default that disagrees with the class would
/// silently change behaviour depending on which one the caller holds. Keep them in sync.</para>
/// </summary>
public interface IRefreshTokenService
{
    Task<string> GenerateRefreshTokenAsync(long userId, AuthMethodEnum authMethod, bool stayLoggedIn = true, string ipAddress = "unknown", bool isExtensionClient = false,
        string? userAgent = null);

    /// <summary>
    /// Atomically revokes <paramref name="oldRefreshToken"/> and issues its replacement, returning the
    /// new raw token — or <c>null</c> if the old token was already revoked (a replay, or the losing
    /// side of a concurrent refresh), in which case nothing is issued.
    /// </summary>
    Task<string?> RotateRefreshTokenAsync(string oldRefreshToken, long userId, AuthMethodEnum authMethod, bool stayLoggedIn, string ipAddress = "unknown", bool isExtensionClient = false,
        string? userAgent = null);

    Task<(bool IsValid, AuthMethodEnum AuthMethod, bool IsStayLoggedIn, bool IsExtensionClient, long? UserId, string? ErrorMessage)> ValidateRefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken, string ipAddress = "unknown");
    Task RevokeAllUserTokensAsync(long userId, string ipAddress = "unknown");
    Task<int> CleanupExpiredTokensAsync();
    Task<IList<RefreshTokenSessionData>> GetUserSessionsAsync(long userId);
    Task<(bool Found, bool IsCurrent)> RevokeSessionByIdAsync(long sessionId, long userId, string? currentTokenHash, string ipAddress = "unknown");
    Task RevokeAllExceptCurrentAsync(long userId, string? currentTokenHash, string ipAddress = "unknown");
}