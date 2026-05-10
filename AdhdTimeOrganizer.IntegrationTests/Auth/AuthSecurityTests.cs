using System.Net;
using System.Net.Http.Json;
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

public class AuthSecurityTests : IntegrationTestBase
{
    private User? _dedicatedUser;

    public AuthSecurityTests(TestWebApplicationFactory factory) : base(factory) { }

    // ── 2FA ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoFa_DirectCall_WithoutPreAuthToken_Returns401()
    {
        var anonClient = Factory.CreateClient();
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/2fa");
        request.Headers.Add("X-Forwarded-For", "10.0.0.1");
        request.Content = JsonContent.Create(payload);

        var response = await anonClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TwoFa_WithTamperedPreAuthToken_Returns401()
    {
        var anonClient = Factory.CreateClient();
        var payload = new { Email = "anyone@test.com", Token = "123456", StayLoggedIn = false, PendingAuthToken = "tampered.garbage.token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/2fa");
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

        using (var scope = Factory.Services.CreateScope())
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

        var anonClient = Factory.CreateClient();
        var payload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
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

        using (var scope = Factory.Services.CreateScope())
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

        var cookieClient = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });
        var loginPayload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        loginRequest.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        loginRequest.Content = JsonContent.Create(loginPayload);
        (await cookieClient.SendAsync(loginRequest)).EnsureSuccessStatusCode();

        var userId = _dedicatedUser.Id;
        using (var db = CreateDbContext())
        {
            (await db.RefreshTokens.AnyAsync(r => r.UserId == userId && !r.IsRevoked))
                .Should().BeTrue("login must create a refresh token");
        }

        var changePayload = new { CurrentPassword = password, NewPassword = newPassword };
        (await cookieClient.PatchAsJsonAsync("/api/user/password", changePayload))
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
        var response = await Client.PostAsync("/api/auth/logout", null);
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
        using (var scope = Factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<domain.extServiceContract.user.auth.IRefreshTokenService>();
            await svc.GenerateRefreshTokenAsync(userId, isExtensionClient: false,
                domain.extServiceContract.user.auth.AuthMethodEnum.Password, stayLoggedIn: false);
            await svc.GenerateRefreshTokenAsync(userId, isExtensionClient: false,
                domain.extServiceContract.user.auth.AuthMethodEnum.Password, stayLoggedIn: false);
        }

        var response = await Client.PostAsync("/api/auth/logout-all", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

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
        var anonClient = Factory.CreateClient();

        var response = await anonClient.GetAsync("/api/auth/confirm-email?userId=999999999&token=fakeToken");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Invalid or expired confirmation link");
        body.Should().NotContain("User not found");
    }

    [Fact]
    public async Task ConfirmEmail_PostMethod_Returns405()
    {
        var anonClient = Factory.CreateClient();

        var response = await anonClient.PostAsync("/api/auth/confirm-email?userId=1&token=abc", null);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ResendConfirmationEmail_UnknownUserId_Returns200_SameAsKnown()
    {
        var anonClient = Factory.CreateClient();

        var response = await anonClient.PostAsync("/api/auth/resend-confirmation-email/999999999", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Confirmation email sent if applicable");
    }

    // ── Forgot password ───────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_WithRecaptchaToken_Returns204()
    {
        var anonClient = Factory.CreateClient();
        var payload = new { Email = TestEmail, RecaptchaToken = "mocked-token" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgotten-password");
        request.Headers.Add("X-Forwarded-For", "10.0.0.2");
        request.Content = JsonContent.Create(payload);

        var response = await anonClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ForgotPassword_MissingRecaptchaToken_ReturnsError()
    {
        var anonClient = Factory.CreateClient();
        // Omit RecaptchaToken — it is `required` so the request is malformed
        var payload = new { Email = TestEmail };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgotten-password");
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
            using var scope = Factory.Services.CreateScope();
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
