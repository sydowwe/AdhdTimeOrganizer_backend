using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Services;

public class RefreshTokenServiceTests : IntegrationTestBase
{
    public RefreshTokenServiceTests(TestWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public async Task GenerateRefreshToken_ReturnsNonEmptyOpaqueString()
    {
        var userId = await GetTestUserIdAsync();
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var token = await service.GenerateRefreshTokenAsync(userId, isExtensionClient: false, AuthMethodEnum.Password, stayLoggedIn: false);

        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateRefreshToken_StayLoggedIn_CreatesLongLivedToken()
    {
        var userId = await GetTestUserIdAsync();
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        await service.GenerateRefreshTokenAsync(userId, isExtensionClient: false, AuthMethodEnum.Password, stayLoggedIn: true);

        using var db = CreateDbContext();
        var stored = await db.RefreshTokens
            .Where(r => r.UserId == userId && r.StayLoggedIn)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync();
        stored.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task GenerateRefreshToken_ShortSession_CreatesOneDayToken()
    {
        var userId = await GetTestUserIdAsync();
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        await service.GenerateRefreshTokenAsync(userId, isExtensionClient: false, AuthMethodEnum.Password, stayLoggedIn: false);

        using var db = CreateDbContext();
        var stored = await db.RefreshTokens
            .Where(r => r.UserId == userId && !r.StayLoggedIn && !r.IsExtensionClient)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync();
        stored.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ValidateRefreshToken_ValidToken_ReturnsTrueWithUser()
    {
        var userId = await GetTestUserIdAsync();
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = await service.GenerateRefreshTokenAsync(userId, false, AuthMethodEnum.Password);

        var (isValid, _, _, user, error) = await service.ValidateRefreshTokenAsync(token);

        isValid.Should().BeTrue();
        user.Should().NotBeNull();
        user!.Id.Should().Be(userId);
        error.Should().BeNull();
    }

    [Fact]
    public async Task ValidateRefreshToken_UnknownToken_ReturnsFalse()
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        var (isValid, _, _, user, error) = await service.ValidateRefreshTokenAsync("completely-unknown-token");

        isValid.Should().BeFalse();
        user.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateRefreshToken_RevokedToken_ReturnsFalseAndRevokesAllUserTokens()
    {
        var userId = await GetTestUserIdAsync();
        string token1, token2;
        {
            using var scope = Factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            token1 = await service.GenerateRefreshTokenAsync(userId, false, AuthMethodEnum.Password);
            token2 = await service.GenerateRefreshTokenAsync(userId, false, AuthMethodEnum.Password);
            await service.RevokeRefreshTokenAsync(token1);
        }

        // Attempting to validate the revoked token triggers the reuse-attack protection
        using var scope2 = Factory.Services.CreateScope();
        var service2 = scope2.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var (isValid, _, _, user, _) = await service2.ValidateRefreshTokenAsync(token1);

        isValid.Should().BeFalse();
        user.Should().BeNull();

        using var db = CreateDbContext();
        var anyActive = await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked);
        anyActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeRefreshToken_SetsIsRevokedAndRevokedAt()
    {
        var userId = await GetTestUserIdAsync();
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = await service.GenerateRefreshTokenAsync(userId, false, AuthMethodEnum.Password);

        await service.RevokeRefreshTokenAsync(token, ipAddress: "127.0.0.1");

        using var db = CreateDbContext();
        var stored = await db.RefreshTokens
            .Where(r => r.UserId == userId && r.IsRevoked)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync();
        stored.IsRevoked.Should().BeTrue();
        stored.RevokedAt.Should().NotBeNull();
        stored.RevokedByIp.Should().Be("127.0.0.1");
    }

    [Fact]
    public async Task RevokeAllUserTokens_RevokesEveryActiveToken()
    {
        var userId = await GetTestUserIdAsync();
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        await service.GenerateRefreshTokenAsync(userId, false, AuthMethodEnum.Password);
        await service.GenerateRefreshTokenAsync(userId, false, AuthMethodEnum.Password);

        await service.RevokeAllUserTokensAsync(userId);

        using var db = CreateDbContext();
        var anyActive = await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked);
        anyActive.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupExpiredTokens_WhenNoOldTokensExist_ReturnsZero()
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();

        // All tokens in the DB are recent (just created by login/other tests), so nothing qualifies
        var count = await service.CleanupExpiredTokensAsync();

        count.Should().Be(0);
    }

    public override async Task DisposeAsync()
    {
        var userId = await GetTestUserIdAsync();
        using var db = CreateDbContext();
        var tokens = await db.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        db.RefreshTokens.RemoveRange(tokens);
        await db.SaveChangesAsync();
    }
}
