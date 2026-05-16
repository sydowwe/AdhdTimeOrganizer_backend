using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
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

    // ── Password change ───────────────────────────────────────────────────────

    [Fact]
    public async Task PasswordChange_RevokesAllRefreshTokens()
    {
        const string email = "pwchange-sec-test@integration.com";
        const string password = "Test@1234!";
        const string newPassword = "NewTest@5678!";

        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            _dedicatedUser = new User
            {
                Email = email,
                EmailConfirmed = true,
                CurrentLocale = AvailableLocales.En,
                Timezone = TimeZoneInfo.Utc
            };
            await userManager.CreateAsync(_dedicatedUser, password);
            await userManager.AddToRoleAsync(_dedicatedUser, "User");
        }

        var cookieClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = baseUrl
        });
        var loginPayload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        loginRequest.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        loginRequest.Content = JsonContent.Create(loginPayload);
        (await cookieClient.SendAsync(loginRequest)).EnsureSuccessStatusCode();

        var userId = _dedicatedUser.Id;
        using (var db = CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeTrue("login must create a refresh token");
        }

        var changePayload = new { Password = password, NewPassword = newPassword };
        (await cookieClient.PatchAsJsonAsync("user/password", changePayload))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var db = CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeFalse("all refresh tokens must be revoked after password change");
        }
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_RevokesRefreshToken_InDb()
    {
        var userId = await GetTestUserIdAsync();

        using (var db = CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeTrue("login in InitializeAsync must create a refresh token");
        }

        // Client already has auth-token + refresh-token cookies from InitializeAsync login
        var response = await client.PostAsync("auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var db = CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeFalse("logout must revoke the refresh token in the DB");
        }
    }

    [Fact]
    public async Task LogoutAll_RevokesAllRefreshTokens_InDb()
    {
        var userId = await GetTestUserIdAsync();

        // Generate extra tokens directly via the service
        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<domain.extServiceContract.user.auth.IRefreshTokenService>();
            await svc.GenerateRefreshTokenAsync(userId, isExtensionClient: false,
                domain.extServiceContract.user.auth.AuthMethodEnum.Password, stayLoggedIn: false);
            await svc.GenerateRefreshTokenAsync(userId, isExtensionClient: false,
                domain.extServiceContract.user.auth.AuthMethodEnum.Password, stayLoggedIn: false);
        }

        var response = await client.PostAsync("auth/logout-all", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var db = CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeFalse("logout-all must revoke every active refresh token");
        }
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
    public async Task ResendConfirmationEmail_UnknownUserId_Returns200_SameAsKnown()
    {
        var response = await anonClient.PostAsync("auth/resend-confirmation-email/999999999", JsonContent.Create(new { }));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
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