using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public record RefreshTokenSessionData(
    long Id,
    string TokenHash,
    string? UserAgent,
    string? IpAddress,
    DateTime CreatedAt
);

public interface IRefreshTokenService
{
    Task<string> GenerateRefreshTokenAsync(long userId, bool isExtensionClient, AuthMethodEnum authMethod, bool stayLoggedIn = true, string? ipAddress = null, string? userAgent = null);
    Task<(bool IsValid, AuthMethodEnum AuthMethod, bool IsStayLoggedIn, User? User, string? ErrorMessage)> ValidateRefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken, string? ipAddress = null);
    Task RevokeAllUserTokensAsync(long userId, string? ipAddress = null);
    Task<int> CleanupExpiredTokensAsync();
    Task<IList<RefreshTokenSessionData>> GetUserSessionsAsync(long userId);
    Task<(bool Found, bool IsCurrent)> RevokeSessionByIdAsync(long sessionId, long userId, string? currentTokenHash, string? ipAddress);
    Task RevokeAllExceptCurrentAsync(long userId, string? currentTokenHash, string? ipAddress);
}
