using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;

namespace Sydowwe.Framework.infrastructure.extService.user.auth;

public class RefreshTokenService(DbContext dbContext, ILogger<RefreshTokenService> logger)
    : IRefreshTokenService, IScopedService
{
    public DbSet<RefreshToken> DbSet => dbContext.Set<RefreshToken>();

    /// <summary>
    /// Every read/update here targets an explicit user id or token hash, so ambient user scoping must
    /// not apply. A host that puts a global "rows belong to the current user" filter on
    /// <c>IEntityWithUser</c> would otherwise silently narrow these to the *caller* — revoking another
    /// user's sessions (admin action, password reset for someone else, a background job) would report
    /// success while updating nothing, because the filter is applied to <c>ExecuteUpdateAsync</c> too.
    /// </summary>
    private IQueryable<RefreshToken> Tokens => DbSet.IgnoreQueryFilters();

    /// <summary>
    /// How long after a token is rotated a stale concurrent refresh carrying that just-revoked token is
    /// tolerated as a benign race rather than treated as a reuse attack. Covers parallel SPA requests
    /// that all 401 and refresh together; short enough that a genuinely stolen old token is still caught.
    ///
    /// A concurrent refresh burst fires within a few round-trips of each other, so a handful of seconds
    /// is enough to absorb the race. Keeping this tight (vs. the earlier 60s) narrows the window in which
    /// a replayed old token gets a free pass before reuse detection kicks in and nukes the whole family.
    /// </summary>
    private static readonly TimeSpan RotationGraceWindow = TimeSpan.FromSeconds(5);

    public async Task<string> GenerateRefreshTokenAsync(long userId, AuthMethodEnum authMethod, bool stayLoggedIn = true, string ipAddress = "unknown", bool isExtensionClient = false,
        string? userAgent = null)
    {
        var (rawToken, tokenHash) = CreateTokenPair();
        var expiresAt = isExtensionClient || stayLoggedIn ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(1);

        var refreshToken = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            StayLoggedIn = stayLoggedIn,
            AuthMethod = authMethod,
            IsExtensionClient = isExtensionClient,
            UserAgent = userAgent,
            IpAddress = ipAddress
        };

        DbSet.Add(refreshToken);
        await dbContext.SaveChangesAsync();

        logger.LogInformation("Generated refresh token for user {UserId}, extension: {IsExtension}", userId, isExtensionClient);
        return rawToken;
    }

    public async Task<string?> RotateRefreshTokenAsync(string oldRefreshToken, long userId, AuthMethodEnum authMethod, bool stayLoggedIn, string ipAddress = "unknown", bool isExtensionClient = false,
        string? userAgent = null)
    {
        var oldTokenHash = HashToken(oldRefreshToken);
        var (newRawToken, newTokenHash) = CreateTokenPair();
        var expiresAt = isExtensionClient || stayLoggedIn ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddDays(1);

        // Revoke + re-issue must commit together: ExecuteUpdateAsync auto-commits the revoke on its own,
        // so a failed insert afterwards would leave the old token dead with no replacement and silently
        // log the user out. One transaction makes the swap all-or-nothing.
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        // Atomically revoke the old token. The `IsRevoked == false` guard inside a single UPDATE means
        // only one of two concurrent refreshes with the same token can win (rowcount 1); the loser sees
        // 0 rows and is rejected, so a stolen/duplicated token can never spawn two live chains.
        // RefreshToken is [NoAudit], so the ExecuteUpdateAsync audit-interceptor bypass is irrelevant.
        var revokedCount = await Tokens
            .Where(rt => rt.TokenHash == oldTokenHash && !rt.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.RevokedAt, DateTime.UtcNow)
                .SetProperty(rt => rt.RevokedByIp, ipAddress)
                .SetProperty(rt => rt.ReplacedByTokenHash, newTokenHash)
                .SetProperty(rt => rt.ModifiedTimestamp, DateTime.UtcNow));

        if (revokedCount == 0)
        {
            logger.LogWarning("Refresh token rotation rejected for user {UserId} (already revoked or lost a concurrent race)", userId);
            return null;
        }

        DbSet.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = newTokenHash,
            ExpiresAt = expiresAt,
            StayLoggedIn = stayLoggedIn,
            AuthMethod = authMethod,
            IsExtensionClient = isExtensionClient,
            UserAgent = userAgent,
            IpAddress = ipAddress
        });

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation("Rotated refresh token for user {UserId}", userId);
        return newRawToken;
    }

    public async Task<(bool IsValid, AuthMethodEnum AuthMethod, bool IsStayLoggedIn, bool IsExtensionClient, long? UserId, string? ErrorMessage)> ValidateRefreshTokenAsync(string refreshToken)
    {
        var tokenHash = HashToken(refreshToken);

        var storedToken = await Tokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (storedToken == null)
            return (false, AuthMethodEnum.Password, false, false, null, "Invalid refresh token");

        if (storedToken.IsRevoked)
        {
            // Distinguish a legitimate concurrent rotation from a real replay attack. When a SPA fires
            // several requests in parallel right as the 15-min access token expires, each 401 triggers a
            // refresh carrying the SAME token: one wins the rotation, the losers arrive to find it already
            // revoked. A loser has ReplacedByTokenHash set (it was rotated normally, not stolen) and was
            // revoked moments ago — soft-reject just that request; its sibling already issued fresh cookies.
            // Without this, the nuclear RevokeAllUserTokensAsync below would also kill the freshly-minted
            // replacement, force-logging the user out roughly every 15 minutes of active use.
            var withinRotationGrace = storedToken.ReplacedByTokenHash != null &&
                                      storedToken.RevokedAt >= DateTime.UtcNow - RotationGraceWindow;
            if (withinRotationGrace)
                return (false, AuthMethodEnum.Password, false, false, null, "Refresh token already rotated by a concurrent request");

            // A revoked token with no replacement, or reused outside the grace window, is the real
            // reuse-attack signature: invalidate every token for the user.
            logger.LogWarning("Attempted use of revoked token for user {UserId}. Possible token reuse attack.", storedToken.UserId);
            await RevokeAllUserTokensAsync(storedToken.UserId);
            return (false, AuthMethodEnum.Password, false, false, null, "Token has been revoked. All tokens invalidated for security.");
        }

        if (storedToken.ExpiresAt >= DateTime.UtcNow)
            return (true, storedToken.AuthMethod, storedToken.StayLoggedIn, storedToken.IsExtensionClient, storedToken.UserId, null);

        await RevokeRefreshTokenAsync(refreshToken);
        return (false, AuthMethodEnum.Password, false, false, null, "Refresh token expired");
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, string ipAddress = "unknown")
    {
        var tokenHash = HashToken(refreshToken);
        var storedToken = await Tokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

        if (storedToken is { IsRevoked: false })
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = ipAddress;
            await dbContext.SaveChangesAsync();

            logger.LogInformation("Revoked refresh token for user {UserId}", storedToken.UserId);
        }
    }

    public async Task RevokeAllUserTokensAsync(long userId, string ipAddress = "unknown")
    {
        // Single atomic statement: only destroys credentials (issues nothing), so a concurrent run
        // simply converges to the same "all revoked" state without risking a concurrency exception.
        // RefreshToken is [NoAudit], so the ExecuteUpdateAsync audit-interceptor bypass is irrelevant.
        var count = await Tokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(rt => rt.IsRevoked, true)
                .SetProperty(rt => rt.RevokedAt, DateTime.UtcNow)
                .SetProperty(rt => rt.RevokedByIp, ipAddress)
                .SetProperty(rt => rt.ModifiedTimestamp, DateTime.UtcNow));

        logger.LogInformation("Revoked {Count} refresh tokens for user {UserId}", count, userId);
    }

    public async Task<int> CleanupExpiredTokensAsync()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-30);

        var expiredTokens = await Tokens
            .Where(rt => (rt.IsRevoked || rt.ExpiresAt < DateTime.UtcNow) && rt.CreatedTimestamp < cutoffDate)
            .ToListAsync();

        DbSet.RemoveRange(expiredTokens);
        var count = await dbContext.SaveChangesAsync();

        logger.LogInformation("Cleaned up {Count} expired refresh tokens", count);
        return count;
    }

    public async Task<IList<RefreshTokenSessionData>> GetUserSessionsAsync(long userId)
    {
        return await Tokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && rt.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(rt => rt.CreatedTimestamp)
            .Select(rt => new RefreshTokenSessionData(
                rt.Id,
                rt.TokenHash,
                rt.UserAgent,
                rt.IpAddress,
                rt.CreatedTimestamp))
            .ToListAsync();
    }

    public async Task<(bool Found, bool IsCurrent)> RevokeSessionByIdAsync(long sessionId, long userId, string? currentTokenHash, string ipAddress = "unknown")
    {
        var token = await Tokens
            .FirstOrDefaultAsync(rt => rt.Id == sessionId && rt.UserId == userId && !rt.IsRevoked);

        if (token is null)
            return (false, false);

        if (token.TokenHash == currentTokenHash)
            return (true, true);

        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = ipAddress;
        await dbContext.SaveChangesAsync();

        return (true, false);
    }

    public async Task RevokeAllExceptCurrentAsync(long userId, string? currentTokenHash, string ipAddress = "unknown")
    {
        var tokens = await Tokens
            .Where(rt => rt.UserId == userId && !rt.IsRevoked && rt.TokenHash != currentTokenHash)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = ipAddress;
        }

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Revoked {Count} sessions for user {UserId} except current", tokens.Count, userId);
    }

    private static (string RawToken, string Hash) CreateTokenPair()
    {
        var tokenBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        var token = Convert.ToBase64String(tokenBytes);
        return (token, HashToken(token));
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}