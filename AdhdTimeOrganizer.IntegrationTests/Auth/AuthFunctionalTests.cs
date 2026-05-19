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

public class AuthFunctionalTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private User? _dedicatedUser;

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

        var userId = await GetTestUserIdAsync();
        using var db = CreateDbContext();
        var tokens = await db.RefreshTokens.Where(r => r.UserId == userId).ToListAsync();
        db.RefreshTokens.RemoveRange(tokens);
        await db.SaveChangesAsync();
    }
}
