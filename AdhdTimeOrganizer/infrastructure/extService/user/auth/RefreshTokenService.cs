using System.Security.Cryptography;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.infrastructure.extService.user.auth;

public class RefreshTokenService(AppDbContext dbContext, ILogger<RefreshTokenService> logger)
    : IRefreshTokenService, IScopedService
{
    public async Task<string> GenerateRefreshTokenAsync(long userId, bool isExtensionClient, AuthMethodEnum authMethod, bool stayLoggedIn = true, string? ipAddress = null)
    {
        var tokenBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        var token = Convert.ToBase64String(tokenBytes);

        var tokenHash = HashToken(token);

        var expiresAt = isExtensionClient | stayLoggedIn
            ? DateTime.UtcNow.AddDays(30)
            : DateTime.UtcNow.AddDays(1);

        var refreshToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            IsExtensionClient = isExtensionClient,
            StayLoggedIn = stayLoggedIn,
            AuthMethod = authMethod // Add this property
        };

        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Generated refresh token for user {UserId}, extension: {IsExtension}",
            userId, isExtensionClient);

        return token;
    }

    public async Task<(bool IsValid, AuthMethodEnum AuthMethod, bool IsStayLoggedIn, User? User, string? ErrorMessage)> ValidateRefreshTokenAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);

        var storedToken = await dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (storedToken == null)
            return (false, AuthMethodEnum.Password, false, null, "Invalid refresh token");

        if (storedToken.IsRevoked)
        {
            logger.LogWarning("Attempted use of revoked token for user {UserId}. Possible token reuse attack.",
                storedToken.UserId);
            await RevokeAllUserTokensAsync(storedToken.UserId);
            return (false, AuthMethodEnum.Password, false, null, "Token has been revoked. All tokens invalidated for security.");
        }

        if (storedToken.ExpiresAt >= DateTime.UtcNow)
            return (true, storedToken.AuthMethod, storedToken.StayLoggedIn, storedToken.User, null);

        await RevokeRefreshTokenAsync(refreshToken);
        return (false, AuthMethodEnum.Password, false, null, "Refresh token expired");
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, string? ipAddress = null)
    {
        var tokenHash = HashToken(refreshToken);

        var storedToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (storedToken is { IsRevoked: false })
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = ipAddress;
            await dbContext.SaveChangesAsync();

            logger.LogInformation("Revoked refresh token for user {UserId}", storedToken.UserId);
        }
    }

    public async Task RevokeAllUserTokensAsync(long userId, string? ipAddress = null)
    {
        var tokens = await dbContext.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = ipAddress;
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Revoked all refresh tokens for user {UserId}", userId);
    }

    public async Task<int> CleanupExpiredTokensAsync()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);

        var expiredTokens = await dbContext.RefreshTokens
            .Where(rt => (rt.IsRevoked || rt.ExpiresAt < DateTime.UtcNow) && rt.CreatedTimestamp < cutoffDate)
            .ToListAsync();

        dbContext.RefreshTokens.RemoveRange(expiredTokens);
        var count = await dbContext.SaveChangesAsync();

        logger.LogInformation("Cleaned up {Count} expired refresh tokens", count);
        return count;
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}