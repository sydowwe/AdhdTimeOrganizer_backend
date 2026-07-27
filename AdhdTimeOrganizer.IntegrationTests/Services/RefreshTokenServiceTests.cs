using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

[Collection("Postgres")]
public class RefreshTokenServiceTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly long UserId = FakeLoggedUserService.TestUserId;

    private IServiceScope Scope() => Fixture.UnauthenticatedFactory.Services.CreateScope();

    [Fact]
    public async Task GenerateRefreshToken_ReturnsNonEmptyOpaqueString()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var token = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password, false, "unknown");

        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateRefreshToken_StayLoggedIn_CreatesLongLivedToken()
    {
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password, true, "unknown");
        }

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var stored = await db.RefreshTokens
            .Where(r => r.UserId == UserId && r.StayLoggedIn)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync();
        stored.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task GenerateRefreshToken_ShortSession_CreatesOneDayToken()
    {
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password, false, "unknown");
        }

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var stored = await db.RefreshTokens
            .Where(r => r.UserId == UserId && !r.StayLoggedIn && !r.IsExtensionClient)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync();
        stored.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ValidateRefreshToken_ValidToken_ReturnsTrueWithUserId()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);

        var (isValid, _, _, _, userId, error) = await service.ValidateRefreshTokenAsync(token);

        isValid.Should().BeTrue();
        userId.Should().Be(UserId);
        error.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshToken_UnknownToken_ReturnsFalse()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var (isValid, _, _, _, userId, error) = await service.ValidateRefreshTokenAsync("completely-unknown-token");

        isValid.Should().BeFalse();
        userId.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateRefreshToken_RevokedToken_ReturnsFalseAndRevokesAllUserTokens()
    {
        string token1;
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            token1 = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
            await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
            await service.RevokeRefreshTokenAsync(token1);
        }

        // Attempting to validate the revoked token triggers the reuse-attack protection
        using var scope2 = Scope();
        var service2 = scope2.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var (isValid, _, _, _, userId, _) = await service2.ValidateRefreshTokenAsync(token1);

        isValid.Should().BeFalse();
        userId.Should().BeNull();

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var anyActive = await db.RefreshTokens.AnyAsync(r => r.UserId == UserId && !r.IsRevoked);
        anyActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeRefreshToken_SetsIsRevokedAndRevokedAt()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);

        await service.RevokeRefreshTokenAsync(token, "127.0.0.1");

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var stored = await db.RefreshTokens
            .Where(r => r.UserId == UserId && r.IsRevoked)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync();
        stored.IsRevoked.Should().BeTrue();
        stored.RevokedAt.Should().NotBeNull();
        stored.RevokedByIp.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task RevokeAllUserTokens_RevokesEveryActiveToken()
    {
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
            await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
            await service.RevokeAllUserTokensAsync(UserId);
        }

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var anyActive = await db.RefreshTokens.AnyAsync(r => r.UserId == UserId && !r.IsRevoked);
        anyActive.Should().BeFalse();
    }

    // ── Rotation (gained by moving onto the framework service) ────────────────

    [Fact]
    public async Task RotateRefreshToken_IssuesNewTokenAndRevokesOldWithReplacementLink()
    {
        string oldToken;
        string? newToken;
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            oldToken = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
            newToken = await service.RotateRefreshTokenAsync(oldToken, UserId, AuthMethodEnum.Password, true, "127.0.0.1");
        }

        newToken.Should().NotBeNullOrEmpty().And.NotBe(oldToken);

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var rotated = await db.RefreshTokens
            .Where(r => r.UserId == UserId && r.IsRevoked)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync();
        // The replacement link is what lets ValidateRefreshTokenAsync tell a benign concurrent
        // refresh apart from a genuine token-reuse attack.
        rotated.ReplacedByTokenHash.Should().NotBeNull();
        (await db.RefreshTokens.CountAsync(r => r.UserId == UserId && !r.IsRevoked)).Should().Be(1);
    }

    [Fact]
    public async Task RotateRefreshToken_SameTokenTwice_SecondAttemptIsRejected()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);

        (await service.RotateRefreshTokenAsync(token, UserId, AuthMethodEnum.Password, true, "127.0.0.1"))
            .Should().NotBeNull();

        // Replaying the already-rotated token must not mint a second live chain.
        (await service.RotateRefreshTokenAsync(token, UserId, AuthMethodEnum.Password, true, "127.0.0.1"))
            .Should().BeNull();
    }

    [Fact]
    public async Task CleanupExpiredTokens_WhenNoOldTokensExist_ReturnsZero()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        // Respawn reset means the DB is otherwise empty for this test, so nothing qualifies as expired.
        var count = await service.CleanupExpiredTokensAsync();

        count.Should().Be(0);
    }
}