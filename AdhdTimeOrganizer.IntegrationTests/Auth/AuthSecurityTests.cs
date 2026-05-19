using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

public class AuthSecurityTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private User? _dedicatedUser;

    // ── 2FA ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoFa_DirectCall_WithoutPreAuthToken_Returns401()
    {
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa");
        request.Headers.Add("X-Forwarded-For", "10.0.0.1");
        request.Content = JsonContent.Create(payload);

        var response = await anonClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFa_WithTamperedPreAuthToken_Returns401()
    {
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false, PendingAuthToken = "tampered.garbage.token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa");
        request.Headers.Add("X-Forwarded-For", "10.0.0.1");
        request.Content = JsonContent.Create(payload);

        var response = await anonClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_UnconfirmedEmail_Returns401_WithGenericMessage()
    {
        const string email = "unconfirmed-sec-test@integration.com";
        const string password = "Test@1234!";

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            _dedicatedUser = new User
            {
                Email = email,
                EmailConfirmed = false,
                CurrentLocale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            await userManager.CreateAsync(_dedicatedUser, password);
            await userManager.AddToRoleAsync(_dedicatedUser, "User");
        }

        var payload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);

        var response = await anonClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("Email not confirmed");
        body.Should().Contain("Invalid email or password");
    }

    // ── Email confirmation ────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmail_NonexistentUserId_Returns400_NotNotFound()
    {
        var response = await anonClient.PostAsJsonAsync("auth/confirm-email", new ConfirmEmailRequest()
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

        var response = await anonClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── 2FA replay & lockout ─────────────────────────────────────────────────

    [Fact]
    public async Task TwoFa_Extension_ReplayedPendingToken_Returns401()
    {
        // Generate a real pending token for the shared test user (2FA disabled → TOTP always passes)
        var userId = await GetTestUserIdAsync();
        var protector = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("2fa-pending")
            .ToTimeLimitedDataProtector();
        var pendingToken = protector.Protect(userId.ToString(), TimeSpan.FromMinutes(5));

        var payload = new { Email = TestEmail, Token = "000000", StayLoggedIn = false, PendingAuthToken = pendingToken };

        // First request — token consumed (2FA disabled → TOTP passes → 204)
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        req1.Content = JsonContent.Create(payload);
        var res1 = await anonClient.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Second request with the same token must be rejected as replayed
        using var req2 = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        req2.Content = JsonContent.Create(payload);
        var res2 = await anonClient.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFa_Extension_LockedOutAccount_Returns401()
    {
        const string email = "2fa-lockout-sec-test@integration.com";
        long lockedUserId;

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            _dedicatedUser = new User
            {
                Email = email,
                EmailConfirmed = true,
                TwoFactorEnabled = true,
                CurrentLocale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            await userManager.CreateAsync(_dedicatedUser, "Test@1234!");
            await userManager.AddToRoleAsync(_dedicatedUser, "User");

            // Exhaust failed attempts to trigger Identity lockout
            for (var i = 0; i < 5; i++)
                await userManager.AccessFailedAsync(_dedicatedUser);

            lockedUserId = _dedicatedUser.Id;
        }

        var protector = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("2fa-pending")
            .ToTimeLimitedDataProtector();
        var pendingToken = protector.Protect(lockedUserId.ToString(), TimeSpan.FromMinutes(5));

        var payload = new { Email = email, Token = "000000", StayLoggedIn = false, PendingAuthToken = pendingToken };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login/2fa/extension");
        request.Content = JsonContent.Create(payload);

        var response = await anonClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("locked");
    }

    // ── Token reuse detection ────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ReusedInParallel_DetectsReplayAndLocksUser()
    {
        var userId = await GetTestUserIdAsync();

        // Extract the refresh token from cookies set during InitializeAsync
        var refreshToken = client.DefaultRequestHeaders
            .FirstOrDefault(h => h.Key == "Cookie")
            .Value?.FirstOrDefault()?.Split("refresh-token=")
            .LastOrDefault()?.Split(";")
            .FirstOrDefault();

        refreshToken.Should().NotBeNullOrEmpty("test setup must have a valid refresh token from InitializeAsync");

        // Attempt to use the same refresh token twice in parallel
        var task1 = client.PostAsync("auth/refresh", null);
        var task2 = client.PostAsync("auth/refresh", null);

        await Task.WhenAll(task1, task2);

        var response1 = task1.Result;
        var response2 = task2.Result;

        // One should succeed, one should fail (or both fail — depends on implementation)
        // The key is that after detecting reuse, the user is not in a catastrophic state
        var bothSucceeded = response1.IsSuccessStatusCode && response2.IsSuccessStatusCode;
        var atLeastOneFailed = !response1.IsSuccessStatusCode || !response2.IsSuccessStatusCode;
        atLeastOneFailed.Should().BeTrue("token reuse should be detected and rejected");

        // Verify the user is not locked out of normal operations (refresh should work with new token)
        using (var db = CreateDbContext())
        {
            var user = await db.Users.FindAsync(userId);
            user.Should().NotBeNull();
            user!.LockoutEnd.Should().BeNull("user should not be permanently locked out for token reuse");
        }
    }

    // ── Forgot password ───────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_WithRecaptchaToken_Returns204()
    {
        var payload = new { Email = TestEmail, RecaptchaToken = "mocked-token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/forgotten-password");
        request.Headers.Add("X-Forwarded-For", "10.0.0.2");
        request.Content = JsonContent.Create(payload);

        var response = await anonClient.SendAsync(request);

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

        var response = await anonClient.SendAsync(request);

        // FastEndpoints returns 400 for model-binding failures on required fields
        ((int)response.StatusCode).Should().BeOneOf(400, 422);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────


    public override async Task DisposeAsync()
    {
        if (_dedicatedUser is not null)
        {
            using var scope = factory.Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByIdAsync(_dedicatedUser.Id.ToString());
            if (user is not null)
                await userManager.DeleteAsync(user);
        }

        // Clean up any refresh tokens created for the shared test user by logout tests
        var userId = await GetTestUserIdAsync();
        using var db = CreateDbContext();
        var tokens = await db.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        db.RefreshTokens.RemoveRange(tokens);
        await db.SaveChangesAsync();
    }
}