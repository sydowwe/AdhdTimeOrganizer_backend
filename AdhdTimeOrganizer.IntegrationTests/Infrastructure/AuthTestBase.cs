using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.infrastructure.security;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Base for tests that exercise the real login/refresh/logout pipeline end-to-end (cookies, JWT, Identity
/// lockout, 2FA) rather than the RoleTestAuthHandler bypass every other test uses. Builds clients directly
/// off the fixture's unauthenticated factory so requests hit the real auth layer.
/// </summary>
public abstract class AuthTestBase(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    protected const string TestEmail = FakeLoggedUserService.TestUserEmail;
    protected const string TestPassword = AppDbContextFixture.TestUserPassword;

    private static readonly Uri BaseUrl = new("https://localhost/api/");

    protected HttpClient CreateCookieClient() =>
        fixture.UnauthenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
            BaseAddress = BaseUrl
        });

    /// <summary>
    /// Client with no cookie container, so the test controls the <c>Cookie</c> header byte for byte.
    /// Needed when the point of the test is a specific cookie combination (e.g. a stale access token
    /// next to a live refresh token) that a real container would never hand you.
    /// </summary>
    protected HttpClient CreateRawClient() =>
        fixture.UnauthenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
            BaseAddress = BaseUrl
        });

    protected static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var payload = new { Email = email, Password = password, StayLoggedIn = false, RecaptchaToken = "test", Timezone = "UTC" };
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login");
        request.Headers.Add("X-Client-Id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(payload);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A confirmed account of its own, with the <c>User</c> role, that can log in through the real pipeline.
    /// Use it whenever a test revokes sessions: the fixture's shared test user is logged in by other tests,
    /// and revoking its sessions reaches theirs.
    /// </summary>
    protected static async Task<(long UserId, string Email)> CreateRealUserAsync(
        IPostgresFixture fx, string emailPrefix, string password)
    {
        var email = $"{emailPrefix}-{Guid.NewGuid():N}@integration.com";
        await using var db = (AppDbContext)fx.CreateDbContext();
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Without a role assignment the JWT authenticates but 403s on every endpoint (Program.cs's
        // global configurator defaults to Roles("User","Admin","Root")).
        var roleId = await db.Roles.Where(r => r.Name == PortalRoleCatalog.User).Select(r => r.Id).FirstAsync();
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();

        return (user.Id, email);
    }
}
