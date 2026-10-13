using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.jobs;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.entity.user;
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

        var issued = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password, false, "unknown");

        issued.Token.Should().NotBeNullOrEmpty();
        issued.SessionId.Should().NotBeEmpty("every login starts a session with an id of its own");
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
            .FirstAsync( TestContext.Current.CancellationToken);
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
            .FirstAsync( TestContext.Current.CancellationToken);
        stored.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ValidateRefreshToken_ValidToken_ReturnsTrueWithUserId()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).Token;

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

    /// <summary>
    /// A token revoked on purpose (logout, a session revoke, a credential reset) has no replacement link. Its
    /// session is already dead, so presenting it again is refused and touches nothing else. This test used to
    /// pin the opposite: the revoked device's first refresh signed out every other session of the account.
    /// </summary>
    [Fact]
    public async Task ValidateRefreshToken_DeliberatelyRevokedToken_IsRefusedAndLeavesOtherSessionsAlone()
    {
        string revoked;
        IssuedRefreshToken other;
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            revoked = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).Token;
            other = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
            await service.RevokeRefreshTokenAsync(revoked);
        }

        using (var scope = Scope())
        {
            var (isValid, _, _, _, userId, error) = await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .ValidateRefreshTokenAsync(revoked);

            isValid.Should().BeFalse();
            userId.Should().BeNull();
            error.Should().Be("Refresh token has been revoked");
        }

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.RefreshTokens.AnyAsync(r => r.SessionId == other.SessionId && !r.IsRevoked, TestContext.Current.CancellationToken))
            .Should().BeTrue("a deliberately revoked token is not evidence that anyone else holds the account's tokens");
    }

    /// <summary>
    /// The reuse-attack response is unchanged for what it exists for: a <i>rotated</i> token replayed after
    /// the grace window means two parties hold the chain, so every token for the user is revoked.
    /// </summary>
    [Fact]
    public async Task ValidateRefreshToken_RotatedTokenReplayedAfterTheGraceWindow_RevokesAllUserTokens()
    {
        string rotated;
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            rotated = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).Token;
            await service.RotateRefreshTokenAsync(rotated, UserId, AuthMethodEnum.Password, true);
            await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
        }

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
            await db.RefreshTokens
                .Where(r => r.ReplacedByTokenHash != null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAt, DateTime.UtcNow.AddMinutes(-1)), TestContext.Current.CancellationToken);

        using (var scope = Scope())
            (await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>().ValidateRefreshTokenAsync(rotated))
                .IsValid.Should().BeFalse();

        await using var check = (AppDbContext)Fixture.CreateDbContext();
        (await check.RefreshTokens.AnyAsync(r => r.UserId == UserId && !r.IsRevoked, TestContext.Current.CancellationToken))
            .Should().BeFalse("a rotated token replayed outside the grace window is the theft signature");
    }

    [Fact]
    public async Task RevokeRefreshToken_SetsIsRevokedAndRevokedAt()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).Token;

        await service.RevokeRefreshTokenAsync(token, "127.0.0.1");

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var stored = await db.RefreshTokens
            .Where(r => r.UserId == UserId && r.IsRevoked)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync( TestContext.Current.CancellationToken);
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
        var anyActive = await db.RefreshTokens.AnyAsync(r => r.UserId == UserId && !r.IsRevoked, TestContext.Current.CancellationToken);
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
            oldToken = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).Token;
            newToken = (await service.RotateRefreshTokenAsync(oldToken, UserId, AuthMethodEnum.Password, true, "127.0.0.1"))?.Token;
        }

        newToken.Should().NotBeNullOrEmpty().And.NotBe(oldToken);

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var rotated = await db.RefreshTokens
            .Where(r => r.UserId == UserId && r.IsRevoked)
            .OrderByDescending(r => r.CreatedTimestamp)
            .FirstAsync( TestContext.Current.CancellationToken);
        // The replacement link is what lets ValidateRefreshTokenAsync tell a benign concurrent
        // refresh apart from a genuine token-reuse attack.
        rotated.ReplacedByTokenHash.Should().NotBeNull();
        (await db.RefreshTokens.CountAsync(r => r.UserId == UserId && !r.IsRevoked, TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task RotateRefreshToken_SameTokenTwice_SecondAttemptIsRejected()
    {
        using var scope = Scope();
        var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var token = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).Token;

        (await service.RotateRefreshTokenAsync(token, UserId, AuthMethodEnum.Password, true, "127.0.0.1"))
            .Should().NotBeNull();

        // Replaying the already-rotated token must not mint a second live chain.
        (await service.RotateRefreshTokenAsync(token, UserId, AuthMethodEnum.Password, true, "127.0.0.1"))
            .Should().BeNull();
    }

    /// <summary>
    /// The session id is what the access token's <c>sid</c> claim, the session list and the revoke route all
    /// key on. If rotation minted a fresh one, the id a client read would stop matching after its next
    /// refresh (the bug B28 replaced) and a revoke could never reach the access tokens minted since.
    /// </summary>
    [Fact]
    public async Task RotateRefreshToken_CarriesTheSessionIdForward()
    {
        IssuedRefreshToken first;
        IssuedRefreshToken? second, third;
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            first = await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);
            second = await service.RotateRefreshTokenAsync(first.Token, UserId, AuthMethodEnum.Password, true, "127.0.0.1");
            third = await service.RotateRefreshTokenAsync(second!.Token, UserId, AuthMethodEnum.Password, true, "127.0.0.1");
        }

        second!.SessionId.Should().Be(first.SessionId);
        third!.SessionId.Should().Be(first.SessionId);

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.RefreshTokens.CountAsync(r => r.SessionId == first.SessionId, TestContext.Current.CancellationToken))
            .Should().Be(3, "every link of the chain carries the session id, not only the live one");
    }

    /// <summary>
    /// Revoking by session id is also scoped by user, so an id can never reach another account's rows. Driven
    /// by writing a second user's row under the same session id — something the service itself never does.
    /// </summary>
    [Fact]
    public async Task RevokeSessionById_NeverTouchesAnotherUsersRowsWithTheSameSessionId()
    {
        var otherUserId = await SeedOtherUserAsync();

        IssuedRefreshToken mine;
        using (var scope = Scope())
            mine = await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password);

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = otherUserId,
                SessionId = mine.SessionId,
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                StayLoggedIn = false,
                AuthMethod = AuthMethodEnum.Password
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var scope = Scope())
            (await scope.ServiceProvider.GetRequiredService<IRefreshTokenService>()
                .RevokeSessionByIdAsync(mine.SessionId, UserId, currentTokenHash: null))
                .Should().Be((true, false));

        await using var check = (AppDbContext)Fixture.CreateDbContext();
        (await check.RefreshTokens.IgnoreQueryFilters()
                .AnyAsync(r => r.UserId == otherUserId && !r.IsRevoked, TestContext.Current.CancellationToken))
            .Should().BeTrue("a revoke by session id must stay inside the caller's account");
    }

    private async Task<long> SeedOtherUserAsync()
    {
        using var scope = Scope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var email = $"rt-other-{Guid.NewGuid():N}@integration.com";
        var user = new User { Email = email, UserName = email, EmailConfirmed = true, Locale = AvailableLocales.En, Timezone = TimeZoneInfo.Utc };
        (await userManager.CreateAsync(user, AppDbContextFixture.TestUserPassword)).Succeeded.Should().BeTrue();
        return user.Id;
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

    /// <summary>
    /// P2-06 — the cleanup runs as a Scheduler job now, not a hosted loop. Resolved the way the dispatcher resolves it
    /// (by key, out of every registered handler), so a handler that never made it into DI fails here too.
    /// </summary>
    [Fact]
    public async Task RefreshTokenCleanupJob_DeletesTheDeadTokenAndKeepsTheLiveOne()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid deadSession, liveSession;
        using (var scope = Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
            deadSession = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).SessionId;
            liveSession = (await service.GenerateRefreshTokenAsync(UserId, AuthMethodEnum.Password)).SessionId;
        }

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            // Issued 40 days ago and expired 39 days ago: past the service's 30-day grace.
            var issuedAt = DateTime.UtcNow.AddDays(-40);
            await db.RefreshTokens.IgnoreQueryFilters()
                .Where(r => r.SessionId == deadSession)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CreatedTimestamp, issuedAt)
                    .SetProperty(r => r.ExpiresAt, issuedAt.AddDays(1)), ct);
        }

        using (var scope = Scope())
        {
            var handler = scope.ServiceProvider.GetServices<IScheduledJobHandler>()
                .Single(h => h.Key == RefreshTokenCleanupJobHandler.HandlerKey);
            var now = DateTime.UtcNow;
            await handler.ExecuteAsync(new ScheduledJobContext
            {
                ScheduledFireTimeUtc = now,
                ActualFireTimeUtc = now,
                JobKey = handler.Key,
                CorrelationId = "refresh-token-cleanup-job-test",
                TriggerSource = TriggerSource.Manual
            }, ct);
        }

        await using var check = (AppDbContext)Fixture.CreateDbContext();
        var sessions = await check.RefreshTokens.IgnoreQueryFilters()
            .Where(r => r.UserId == UserId)
            .Select(r => r.SessionId)
            .ToListAsync(ct);
        sessions.Should().NotContain(deadSession, "a token expired a month ago is a dead credential");
        sessions.Should().Contain(liveSession, "the cleanup must never sign anyone out");
    }
}