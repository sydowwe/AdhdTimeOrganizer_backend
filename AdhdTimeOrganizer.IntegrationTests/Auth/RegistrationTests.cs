using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using AdhdTimeOrganizer.infrastructure.security;
using Sydowwe.Framework.domain.@enum;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Auth;

/// <summary>
/// Sign-up through the real pipeline. Registration is the one route where a regression blocks every
/// new account, and it had no coverage before <c>BaseRegisterUserEndpoint</c> was introduced — these
/// pin the route, the status codes and the atomic "account + default role + defaults" outcome that
/// <c>UserRegistrationFlow</c> is responsible for.
/// </summary>
[Collection("Postgres")]
public class RegistrationTests(AppDbContextFixture fixture) : AuthTestBase(fixture)
{
    private static object BuildPayload(string email) => new
    {
        Email = email,
        Password = "NewUserPassword1!",
        RecaptchaToken = "test",
        TwoFactorEnabled = false,
        // Numeric: FastEndpoints' request serializer has no string-enum converter attached.
        CurrentLocale = (int)AvailableLocales.En,
        Timezone = "UTC"
    };

    [Fact]
    public async Task Register_CreatesUserWithDefaultRole()
    {
        const string email = "brand-new-user@example.com";
        using var client = CreateCookieClient();

        var response = await client.PostAsJsonAsync("auth/register", BuildPayload(email));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await using var db = fixture.CreateDbContext();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
        user.Should().NotBeNull();

        var roleIds = await db.UserRoles.Where(ur => ur.UserId == user!.Id).Select(ur => ur.RoleId).ToListAsync();
        var roleNames = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Name).ToListAsync();
        roleNames.Should().Contain(PortalRoleCatalog.User);
    }

    [Fact]
    public async Task Register_Returns409_WhenEmailAlreadyRegistered()
    {
        using var client = CreateCookieClient();

        var response = await client.PostAsJsonAsync("auth/register", BuildPayload(TestEmail));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}