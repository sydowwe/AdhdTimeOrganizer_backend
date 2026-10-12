using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// CQ-3, second half — the auth matrix the Core area never had. Every "unauthorized" assertion in this
/// area is a cross-user <b>404</b> produced by the per-user query filter; nothing asserted a plain
/// <b>401</b>, and nothing asserted that the endpoints which declare no roles of their own are
/// authorized at all.
/// </summary>
/// <remarks>
/// <para>
/// <c>CloneActivityEndpoint</c>, <c>QuickEditActivityEndpoint</c> and
/// <c>BaseActivityFormSelectOptionsEndpoint</c> are gated only by <c>Program.cs</c>'s global
/// <c>Endpoints.Configurator</c>, which fills in <c>Roles("User","Admin","Root")</c> and attaches
/// <c>DenyExtensionClients</c> to anything that did not declare its own. Deleting those four lines
/// during a refactor opens three endpoints to any authenticated principal — including extension
/// tokens — with no build error and no other failing test. That is what this file exists to catch.
/// </para>
/// <para>
/// Every case here passes today. They are guards, not gap pins.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class CoreEndpointAuthMatrixTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private const string Password = "Test@1234!";

    /// <summary>
    /// The three rows that matter are <c>quick-edit</c>, <c>clone</c> and <c>form-select-options</c> —
    /// the endpoints with no <c>Roles(...)</c> call of their own. Do not "simplify" the theory by
    /// dropping them; the rest of the list is the cheap part.
    /// </summary>
    [Theory(DisplayName = "CQ-3: no Core route is reachable unauthenticated")]
    [InlineData("GET", "api/activity")]
    [InlineData("GET", "api/activity/1")]
    [InlineData("GET", "api/activity/all-options")]
    [InlineData("GET", "api/activity/form-select-options")]
    [InlineData("POST", "api/activity/grid")]
    [InlineData("POST", "api/activity")]
    [InlineData("PUT", "api/activity/1")]
    [InlineData("DELETE", "api/activity/1")]
    [InlineData("PUT", "api/activity/1/quick-edit")]
    [InlineData("POST", "api/activity/1/clone")]
    [InlineData("PATCH", "api/activity/1/archived")]
    [InlineData("POST", "api/activity/merge")]
    [InlineData("GET", "api/activity-role")]
    [InlineData("GET", "api/activity-role/by-system-key/plannerTask")]
    [InlineData("GET", "api/activity-category")]
    [InlineData("POST", "api/activity-category/batch-delete")]
    [InlineData("GET", "api/timer-preset")]
    [InlineData("GET", "api/pomodoro-timer-preset")]
    public async Task Unauthenticated_EveryCoreRoute_Returns401(string method, string route)
    {
        using var client = CreateUnauthenticatedClient();

        // The auth gate runs before model binding, so an empty body is enough for the write verbs.
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method is "POST" or "PUT" or "PATCH")
            request.Content = JsonContent.Create(new { });
        var response = await client.SendAsync(request, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a missing token must be 401 — a 403 would mean the principal was accepted, a 404 that the route "
            + "is not registered, and both would hide the gate being gone");
    }

    /// <summary>
    /// The deny-by-default half of the configurator, on exactly the endpoints that declare nothing
    /// themselves. Its comment records that an <c>AuthorizationOptions.FallbackPolicy</c> "silently
    /// protected nothing" here — the <c>Roles()</c> call above it gives every endpoint authorization
    /// metadata, so the fallback never ran. That history is the reason to pin this.
    /// </summary>
    [Theory(DisplayName = "CQ-3: an extension token is refused on the endpoints that declare no roles")]
    [InlineData("PUT", "activity/1/quick-edit")]
    [InlineData("POST", "activity/1/clone")]
    [InlineData("GET", "activity/form-select-options")]
    public async Task ExtensionToken_EndpointsWithoutDeclaredRoles_Return403(string method, string route)
    {
        var email = $"core-auth-matrix-extension-{Guid.NewGuid():N}@test.com";
        await CreateUserWithExtensionAccessAsync(email);
        var accessToken = await ExtensionAccessTokenAsync(email);

        using var extensionClient = CreateCookieClient();
        extensionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method is "POST" or "PUT")
            request.Content = JsonContent.Create(new { });
        var response = await extensionClient.SendAsync(request, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "DenyExtensionClients is attached by the same configurator that fills in the roles — an endpoint "
            + "declaring neither depends on it entirely");
    }

    /// <summary>
    /// Nothing in Core is admin-only. This is the pin against "hardening" the area by narrowing
    /// <c>AllowedRoles()</c> to Admin, which would make the app unusable for every account — the exact
    /// failure <c>PortalRoleCatalog</c>'s docstring warns about.
    /// </summary>
    [Theory(DisplayName = "CQ-3: a plain User may reach every Core read")]
    [InlineData("api/activity")]
    [InlineData("api/activity/all-options")]
    [InlineData("api/activity/form-select-options")]
    [InlineData("api/activity-role")]
    [InlineData("api/activity-category")]
    [InlineData("api/timer-preset")]
    [InlineData("api/pomodoro-timer-preset")]
    public async Task UserRole_CoreReads_AreNotForbidden(string route)
    {
        var response = await CreateUserRoleClient().GetAsync(route, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the default tier is User + Admin + Root and every one of these is an ordinary user surface");
    }

    /// <summary>
    /// The two endpoints that <em>do</em> declare <c>AllowedRoles() =&gt; this.GetDefaultRoles()</c>.
    /// Cheap, and it pins that <c>GetDefaultRoles()</c> still resolves to the portal's catalog rather
    /// than the framework's deny-by-default Admin tier.
    /// </summary>
    [Fact(DisplayName = "CQ-3: merge and the archive toggle accept the default roles")]
    public async Task UserRole_MergeAndArchive_AreNotForbidden()
    {
        var client = CreateUserRoleClient();

        var merge = await client.PostAsJsonAsync("api/activity/merge",
            new { SurvivorId = 1L, MergedIds = Array.Empty<long>() }, JsonOpts, CancellationToken);
        merge.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "an empty merged set is a 400, not a role refusal");

        var archive = await client.PatchAsJsonAsync("api/activity/987654321/archived",
            new { IsArchived = true }, JsonOpts, CancellationToken);
        archive.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, "an unknown id is a 404, not a role refusal");
    }

    // ---- extension-token setup (the only other copy lives in ActivityEndpointTests) -----------------

    private async Task<string> ExtensionAccessTokenAsync(string email)
    {
        using var loginClient = CreateCookieClient();
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "auth/extension/login");
        loginRequest.Headers.Add("X-Forwarded-For", Guid.NewGuid().ToString());
        loginRequest.Content = JsonContent.Create(new { Email = email, Password });

        var loginResponse = await loginClient.SendAsync(loginRequest, CancellationToken);
        loginResponse.EnsureSuccessStatusCode();

        var tokens = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        return tokens.GetProperty("accessToken").GetString()!;
    }

    private async Task CreateUserWithExtensionAccessAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<UserRole>>();

        var user = new User
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

        if (await roleManager.FindByNameAsync("User") == null)
            await roleManager.CreateAsync(new UserRole { Name = "User", Description = "User", IsDefault = false, RoleLevel = 1, IsAssignable = true });
        await userManager.AddToRoleAsync(user, "User");
    }
}