using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.application.dto.request.user;
using Sydowwe.Framework.application.dto.response.user;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

[Collection("Postgres")]
public class ExtensionActivityTrackingTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = "Test@1234!";

    [Fact]
    public async Task ExtensionLogin_WithExtensionAccess_ReturnsTokens()
    {
        var email = "extension-user@test.com";
        await CreateUserWithExtensionAccess(email, true);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RequiresTwoFactor.Should().BeFalse();
    }

    [Fact]
    public async Task ExtensionLogin_WithoutExtensionAccess_Returns403()
    {
        var email = "no-extension-user@test.com";
        await CreateUserWithExtensionAccess(email, false);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ReturnsOk()
    {
        var email = "heartbeat-user@test.com";
        await CreateUserWithExtensionAccess(email, true);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var windowStart = DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour).AddMinutes(DateTime.UtcNow.Minute);
        var request = new DesktopActivityWindowDto
        {
            WindowStart = windowStart,
            Entries = new List<DesktopActivityEntryDto>
            {
                new()
                {
                    ProcessName = "test.exe",
                    ProductName = "Test Product",
                    WindowTitle = "Test Window",
                    ExecutablePath = @"C:\test\test.exe",
                    IsFullscreen = false,
                    ActiveSeconds = 30,
                    BackgroundSeconds = 0,
                    IsPlayingSound = false,
                    ActiveMonitor = 0
                }
            }
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AndroidSync_WithExtensionToken_ReturnsOk()
    {
        var email = "android-user@test.com";
        await CreateUserWithExtensionAccess(email, true);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new AndroidSyncRequest
        {
            DeviceId = "test-device",
            SyncedUpToUtc = DateTime.UtcNow,
            Sessions = new List<AndroidSessionItemDto>()
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/android/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403()
    {
        var email = "no-role-user@test.com";
        await CreateUserWithExtensionAccess(email, true, false);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow,
            Entries = new List<DesktopActivityEntryDto>()
        };

        var response = await extensionClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task WebJwt_CannotAccess_ActivityTrackingPolicy_Returns403()
    {
        // web-authenticated via cookie JWT (no ActivityTracking role in claims)
        using var webClient = CreateCookieClient();
        await LoginAsync(webClient, TestEmail, TestPassword);

        var request = new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow,
            Entries = new List<DesktopActivityEntryDto>()
        };

        var response = await webClient.PostAsJsonAsync("activity-tracking/desktop/heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExtensionJwt_CannotAccess_WebOnlyEndpoint_Returns403()
    {
        var email = "extension-webonly-test@test.com";
        await CreateUserWithExtensionAccess(email, true);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        // user/sessions uses the fallback policy which blocks extension clients via ExtensionClientRequirement
        var response = await extensionClient.GetAsync("user/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<ExtensionLoginResponse> ExtensionLoginSuccessAsync(string email)
    {
        var response = await ExtensionLoginAsync(email);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>();
        result.Should().NotBeNull();
        return result!;
    }

    private async Task<HttpResponseMessage> ExtensionLoginAsync(string email)
    {
        using var client = CreateCookieClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/extension/login");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new ExtensionLoginRequest { Email = email, Password = Password });
        return await client.SendAsync(request);
    }

    private async Task CreateUserWithExtensionAccess(string email, bool hasExtensionAccess, bool hasRole = true)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<UserRole>>();

        var user = await userManager.FindByEmailAsync(email);
        if (user != null)
            return;

        user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            HasExtensionAccess = hasExtensionAccess,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };

        await userManager.CreateAsync(user, Password);

        string[] roles = hasRole ? ["User"] : [];
        foreach (var role in roles)
        {
            if (await roleManager.FindByNameAsync(role) == null)
                await roleManager.CreateAsync(new UserRole
                {
                    Name = role,
                    Description = role,
                    IsDefault = false,
                    RoleLevel = 1,
                    IsAssignable = true
                });

            await userManager.AddToRoleAsync(user, role);
        }
    }
}