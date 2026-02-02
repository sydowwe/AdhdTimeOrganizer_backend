using AdhdTimeOrganizer.domain.model.entity.user;

namespace AdhdTimeOrganizer.domain.extServiceContract.user.auth;

public interface IRefreshTokenService
{
    Task<string> GenerateRefreshTokenAsync(long userId, bool isExtensionClient, AuthMethodEnum authMethod, bool stayLoggedIn = true, string? ipAddress = null);
    Task<(bool IsValid, AuthMethodEnum AuthMethod, bool IsStayLoggedIn, User? User, string? ErrorMessage)> ValidateRefreshTokenAsync(string refreshToken);
    Task RevokeRefreshTokenAsync(string refreshToken, string? ipAddress = null);
    Task RevokeAllUserTokensAsync(long userId, string? ipAddress = null);
    Task<int> CleanupExpiredTokensAsync();
}
