using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.application.dto.request.user;
using AdhdTimeOrganizer.application.dto.response.user;
using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

public class ExtensionActivityTrackingTests(TestWebApplicationFactory factory) : IntegrationTestBase(factory)
{
    private const string Password = "Test@1234!";

    [Fact]
    public async Task ExtensionLogin_WithExtensionAccess_ReturnsTokens()
    {
        var email = "extension-user@test.com";
        await CreateUserWithExtensionAccess(email, hasExtensionAccess: true);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>();
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.RequiresTwoFactor.Should().BeFalse();
    }

    [Fact]
    public async Task ExtensionLogin_WithoutExtensionAccess_Returns403()
    {
        var email = "no-extension-user@test.com";
        await CreateUserWithExtensionAccess(email, hasExtensionAccess: false);

        var response = await ExtensionLoginAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ReturnsOk()
    {
        var email = "heartbeat-user@test.com";
        await CreateUserWithExtensionAccess(email, hasExtensionAccess: true);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        anonClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow,
            Entries = new List<DesktopActivityEntryDto>()
        };

        var response = await anonClient.PostAsJsonAsync("heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AndroidSync_WithExtensionToken_ReturnsOk()
    {
        var email = "android-user@test.com";
        await CreateUserWithExtensionAccess(email, hasExtensionAccess: true);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        anonClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new AndroidSyncRequest
        {
            DeviceId = "test-device",
            SyncedUpToUtc = DateTime.UtcNow,
            Sessions = new List<AndroidSessionItemDto>()
        };

        var response = await anonClient.PostAsJsonAsync("activity-tracking/android/sync", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Heartbeat_WithExtensionToken_ButNoActivityTrackingRole_Returns403()
    {
        var email = "no-role-user@test.com";
        await CreateUserWithExtensionAccess(email, hasExtensionAccess: true, hasRole: false);

        var loginResult = await ExtensionLoginSuccessAsync(email);

        var extensionClient = factory.CreateClient();
        extensionClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginResult.AccessToken);

        var request = new DesktopActivityWindowDto
        {
            WindowStart = DateTime.UtcNow,
            Entries = new List<DesktopActivityEntryDto>()
        };

        var response = await extensionClient.PostAsJsonAsync("heartbeat", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<ExtensionLoginResponse> ExtensionLoginSuccessAsync(string email)
    {
        var response = await ExtensionLoginAsync(email);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExtensionLoginResponse>();
        result.Should().NotBeNull();
        return result;
    }

    private async Task<HttpResponseMessage> ExtensionLoginAsync(string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/extension/login");
        request.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new ExtensionLoginRequest { Email = email, Password = Password });
        return await anonClient.SendAsync(request);
    }

    private async Task CreateUserWithExtensionAccess(string email, bool hasExtensionAccess, bool hasRole = true)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<UserRole>>();

        var user = await userManager.FindByEmailAsync(email);
        if (user != null) return;

        user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            HasExtensionAccess = hasExtensionAccess,
            CurrentLocale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };

        await userManager.CreateAsync(user, Password);

        string[] roles = hasRole ? ["User"] : [];
        foreach (var role in roles)
        {
            if (await roleManager.FindByNameAsync(role) == null)
            {
                await roleManager.CreateAsync(new UserRole
                {
                    Name = role,
                    Description = role,
                    IsDefault = false,
                    RoleLevel = 1,
                    IsAssignable = true
                });
            }

            await userManager.AddToRoleAsync(user, role);
        }
    }
}