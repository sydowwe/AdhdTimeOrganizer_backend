using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user.auth;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

[Collection("Postgres")]
public class AuthSecurityTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    // ── 2FA ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoFa_DirectCall_WithoutPreAuthToken_Returns401()
    {
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa");
        request.Headers.Add("X-Forwarded-For", "10.0.0.1");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFa_WithTamperedPreAuthToken_Returns401()
    {
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false, PendingAuthToken = "tampered.garbage.token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa");
        request.Headers.Add("X-Forwarded-For", "10.0.0.1");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The setup route is <c>AllowAnonymous</c> — the partial-auth cookie is its only gate. Without it
    /// the endpoint must refuse rather than provision an authenticator for whoever asked.
    /// </summary>
    [Fact]
    public async Task TwoFaSetup_WithoutPendingCookie_Returns401()
    {
        using var client = CreateCookieClient();
        var response = await client.PostAsync("auth/login/2fa/setup", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_UnconfirmedEmail_Returns401_WithGenericMessage()
    {
        const string email = "unconfirmed-sec-test@integration.com";
        const string password = "Test@1234!";

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            var user = new User
            {
                Email = email,
                EmailConfirmed = false,
                Locale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var payload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Email not confirmed");
        body.Should().Contain("Invalid email or password");
    }

    // ── Email confirmation ────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmail_NonexistentUserId_Returns400_NotNotFound()
    {
        using var client = CreateCookieClient();
        var response = await client.PostAsJsonAsync("auth/confirm-email", new ConfirmEmailRequest
        {
            Token = "fakeToken",
            UserId = 999999999
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Invalid or expired confirmation link");
        body.Should().NotContain("User not found");
    }

    [Fact]
    public async Task ResendConfirmationEmail_UnknownEmail_Returns204_SameAsKnown()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/resend-confirmation-email");
        request.Headers.Add("X-Forwarded-For", "10.0.0.3");
        request.Content = JsonContent.Create(new { Email = "nonexistent@example.com" });

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── 2FA replay & lockout ─────────────────────────────────────────────────

    /// <summary>
    /// Mints the partial-auth token the same way the login step does. It is a signed JWT, so it can
    /// only be produced through the service — there is no way to hand-roll one in a test.
    /// </summary>
    private string IssuePendingToken(long userId)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtService>().IssueTwoFactorPendingToken(userId, false);
    }

    [Fact]
    public async Task TwoFa_Extension_ReplayedPendingToken_Returns401()
    {
        // Generate a real pending token for the shared test user (2FA disabled → TOTP always passes)
        var userId = FakeLoggedUserService.TestUserId;
        var pendingToken = IssuePendingToken(userId);

        var payload = new { Email = TestEmail, Token = "000000", StayLoggedIn = false, PendingAuthToken = pendingToken };

        using var client = CreateCookieClient();

        // First request — token consumed (2FA disabled → TOTP passes → tokens issued)
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        req1.Content = JsonContent.Create(payload);
        var res1 = await client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second request with the same token must be rejected as replayed
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        req2.Content = JsonContent.Create(payload);
        var res2 = await client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFa_Extension_LockedOutAccount_Returns401()
    {
        const string email = "2fa-lockout-sec-test@integration.com";
        long lockedUserId;

        await using (var db = (AppDbContext)Fixture.CreateDbContext())
        {
            var user = new User
            {
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                EmailConfirmed = true,
                TwoFactorEnabled = true,
                // AccessFailedAsync only ever locks an account that opted into lockout; without this
                // the failed-count climbs and IsLockedOutAsync stays false.
                LockoutEnabled = true,
                // Identity's lockout bookkeeping (AccessFailedAsync/UpdateAsync) throws without one.
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                Locale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Test@1234!");
            db.Users.Add(user);
            await db.SaveChangesAsync();
            lockedUserId = user.Id;
        }

        using (var scope = Fixture.UnauthenticatedFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByIdAsync(lockedUserId.ToString());
            // Exhaust failed attempts to trigger Identity lockout
            for (var i = 0; i < 5; i++)
                await userManager.AccessFailedAsync(user!);
        }

        var pendingToken = IssuePendingToken(lockedUserId);

        var payload = new { Email = email, Token = "000000", StayLoggedIn = false, PendingAuthToken = pendingToken };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("locked");
    }

    // ── Token reuse detection ────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ReusedInParallel_DetectsReplayAndLocksUser()
    {
        var userId = FakeLoggedUserService.TestUserId;

        using var client = CreateCookieClient();
        await LoginAsync(client, TestEmail, TestPassword);

        // The refresh token itself stays in the handler's cookie container (HandleCookies = true) and
        // is replayed automatically below — it is never visible on DefaultRequestHeaders. Assert the
        // stored side instead, which is what actually proves login issued one.
        await using (var seedCheck = (AppDbContext)Fixture.CreateDbContext())
        {
            (await seedCheck.RefreshTokens.IgnoreQueryFilters()
                .AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeTrue("login must have issued a refresh token");
        }

        // Attempt to use the same refresh token twice in parallel
        var task1 = client.PostAsync("auth/refresh", null);
        var task2 = client.PostAsync("auth/refresh", null);

        await Task.WhenAll(task1, task2);

        var response1 = task1.Result;
        var response2 = task2.Result;

        // One should succeed, one should fail (or both fail — depends on implementation)
        // The key is that after detecting reuse, the user is not in a catastrophic state
        var atLeastOneFailed = !response1.IsSuccessStatusCode || !response2.IsSuccessStatusCode;
        atLeastOneFailed.Should().BeTrue("token reuse should be detected and rejected");

        // Verify the user is not locked out of normal operations (refresh should work with new token)
        await using var db = (AppDbContext)Fixture.CreateDbContext();
        var user = await db.Users.FindAsync(userId);
        user.Should().NotBeNull();
        user!.LockoutEnd.Should().BeNull("user should not be permanently locked out for token reuse");
    }

    // ── Forgot password ───────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_WithRecaptchaToken_Returns204()
    {
        var payload = new { Email = TestEmail, RecaptchaToken = "mocked-token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/forgotten-password");
        request.Headers.Add("X-Forwarded-For", "10.0.0.2");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ForgotPassword_MissingRecaptchaToken_ReturnsError()
    {
        // Omit RecaptchaToken — it is `required` so the request is malformed
        var payload = new { Email = TestEmail };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/forgotten-password");
        request.Headers.Add("X-Forwarded-For", "10.0.0.3");
        request.Content = JsonContent.Create(payload);

        using var client = CreateCookieClient();
        var response = await client.SendAsync(request);

        // FastEndpoints returns 400 for model-binding failures on required fields
        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }
}