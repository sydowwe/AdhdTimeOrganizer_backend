using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.extServiceContract.user.auth;
using AdhdTimeOrganizer.infrastructure.extService.user.auth;
using AdhdTimeOrganizer.infrastructure.persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sydowwe.Framework.domain.result;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

/// <summary>
/// TEST-8 -- <c>GoogleSignInEndpoint</c>. <c>IGoogleSignInService</c> is not faked anywhere else in the
/// suite (unlike <c>IGoogleCalendarService</c>, see <c>SyncCalendarToGoogleTests</c>), so every test here
/// swaps it per-factory via <c>CreateFactory(null, ...)</c> -- <c>null</c> roles leaves the real auth
/// layer in place, which is what this <c>AllowAnonymous</c> route needs to be reachable at all while still
/// exercising the real Identity/JWT pipeline underneath it.
/// </summary>
[Collection("Postgres")]
public class GoogleSignInTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Route = "api/auth/login/google";

    private static Mock<IGoogleSignInService> StubSignIn(Result<GoogleUserInfo> result)
    {
        var mock = new Mock<IGoogleSignInService>(MockBehavior.Strict);
        mock.Setup(s => s.GetUserInfoFromGoogleSignInCode(It.IsAny<string>())).ReturnsAsync(result);
        return mock;
    }

    private static GoogleUserInfo Info(string email, string userId) => new()
    {
        Email = email,
        UserId = userId,
        EmailVerified = true,
        TokenIssuedAt = DateTime.UtcNow,
        TokenExpiresAt = DateTime.UtcNow.AddHours(1)
    };

    private ITestClientFactory CreateFactoryWithSignIn(Mock<IGoogleSignInService> mock) =>
        CreateFactory(null, configureServices: services =>
        {
            services.RemoveAll<IGoogleSignInService>();
            services.AddSingleton(mock.Object);
        });

    [Fact(DisplayName = "A brand-new Google email registers an account and signs it in")]
    public async Task NewUser_RegistersAndSignsIn()
    {
        const string email = "new-google-user@test.com";
        var mock = StubSignIn(Result<GoogleUserInfo>.Successful(Info(email, "google-sub-new-1")));

        await using var factory = CreateFactoryWithSignIn(mock);
        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { Timezone = "UTC", Code = "irrelevant-code" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GoogleSignInResponse>(JsonOpts, CancellationToken);
        body!.Email.Should().Be(email);
        body.RequiresTwoFactor.Should().BeFalse("a federated login satisfies 2FA by default");
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue("a full sign-in must issue the auth cookie");
        cookies!.Should().Contain(c => c.StartsWith("auth-token="));

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email, CancellationToken);
        user.GoogleOAuthUserId.Should().Be("google-sub-new-1");
        user.EmailConfirmed.Should().BeTrue("Google already vouched for the address");
    }

    [Fact(DisplayName = "An account already linked to this Google id signs in without re-registering")]
    public async Task ExistingLinkedUser_SignsIn()
    {
        const string email = "linked-google-user@test.com";
        const string googleId = "google-sub-linked-1";
        await SeedUserAsync(email, googleOAuthUserId: googleId);

        var mock = StubSignIn(Result<GoogleUserInfo>.Successful(Info(email, googleId)));
        await using var factory = CreateFactoryWithSignIn(mock);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { Timezone = "UTC", Code = "irrelevant-code" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        (await db.Users.IgnoreQueryFilters().CountAsync(u => u.Email == email, CancellationToken))
            .Should().Be(1, "signing in with an already-linked account must not create a second row");
    }

    [Fact(DisplayName = "An existing password account with no Google link cannot be signed into via Google")]
    public async Task ExistingUnlinkedUser_Returns409_AndStaysUnlinked()
    {
        const string email = "password-only-user@test.com";
        await SeedUserAsync(email, googleOAuthUserId: null);

        var mock = StubSignIn(Result<GoogleUserInfo>.Successful(Info(email, "google-sub-hijack-1")));
        await using var factory = CreateFactoryWithSignIn(mock);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { Timezone = "UTC", Code = "irrelevant-code" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "an unlinked address must not be silently claimed by whoever controls that Google account");

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email, CancellationToken);
        user.GoogleOAuthUserId.Should().BeNull("a failed sign-in attempt must not link the account as a side effect");
    }

    [Fact(DisplayName = "A Google id that no longer matches the linked account is rejected, not re-linked")]
    public async Task LinkedUserWithMismatchedGoogleId_Returns409_AndLinkUnchanged()
    {
        const string email = "relinked-google-user@test.com";
        const string originalGoogleId = "google-sub-original-1";
        await SeedUserAsync(email, googleOAuthUserId: originalGoogleId);

        var mock = StubSignIn(Result<GoogleUserInfo>.Successful(Info(email, "google-sub-different-1")));
        await using var factory = CreateFactoryWithSignIn(mock);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { Timezone = "UTC", Code = "irrelevant-code" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email, CancellationToken);
        user.GoogleOAuthUserId.Should().Be(originalGoogleId, "the link must not silently move to a different Google account");
    }

    [Fact(DisplayName = "A failed Google token validation is a clean 400 and creates nothing")]
    public async Task FailedTokenValidation_Returns400_NoUserCreated()
    {
        var mock = StubSignIn(Result<GoogleUserInfo>.Error(ResultErrorType.BadRequest, "Invalid token payload"));
        await using var factory = CreateFactoryWithSignIn(mock);

        await using var before = (AppDbContext)Fixture.CreateDbContext();
        var usersBefore = await before.Users.IgnoreQueryFilters().CountAsync(CancellationToken);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { Timezone = "UTC", Code = "bad-code" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse("a rejected sign-in must not issue an auth cookie");

        await using var after = (AppDbContext)Fixture.CreateDbContext();
        (await after.Users.IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(usersBefore, "a failed token validation must not silently create an account");
    }

    private async Task SeedUserAsync(string email, string? googleOAuthUserId)
    {
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            GoogleOAuthUserId = googleOAuthUserId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Test@1234!");
        db.Users.Add(user);
        await db.SaveChangesAsync(CancellationToken);
    }
}
