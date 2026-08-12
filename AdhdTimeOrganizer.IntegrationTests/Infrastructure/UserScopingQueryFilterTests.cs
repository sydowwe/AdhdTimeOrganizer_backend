using System.Net.Http.Json;
using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Infrastructure;

/// <summary>
/// Pins that the global <c>IEntityWithUser</c> query filter built by
/// <c>UserDbContextExtensions.ApplyUserQueryFilters</c> resolves the current user <b>per execution</b>
/// rather than baking one user's id into the cached SQL.
///
/// <para><b>The bug this exists to prevent.</b> The filter is hand-built from
/// <c>Expression.Constant(loggedUserService)</c>. A member read off a constant is an evaluatable
/// subtree with no captured variable and no reference to the DbContext, so EF evaluates it while
/// compiling the query and inlines the result as a <i>literal</i>. The compiled query is then cached
/// (per EF internal service provider, which every host configuring the DbContext identically shares),
/// so every later execution of that shape — any user, any request, any host in the process — runs with
/// the <i>first</i> caller's user id. Nothing throws and nothing logs; the second user simply reads the
/// first user's rows, or none of their own.</para>
///
/// <para>Both tests below failed against the unfixed builder. Until this file existed the only thing in
/// the suite that noticed was
/// <c>ExtensionActivityTrackingTests.Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask</c>,
/// and only when <c>ActivityTimeAutomationTests</c> happened to run first and compile the shapes as
/// user 999 — i.e. by luck of ordering.</para>
/// </summary>
[Collection("Postgres")]
public class UserScopingQueryFilterTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Password = "Test@1234!";

    /// <summary>
    /// The SQL-level assertion, and the one that does not depend on run order at all: whatever user is
    /// ambient when a user-scoped shape is compiled, that id must reach the database as a
    /// <b>parameter</b>. A literal in the generated SQL is the bug, because the SQL is what gets cached.
    /// </summary>
    [Fact]
    public void UserScopedQuery_SendsTheUserIdAsAParameter_NotAsALiteral()
    {
        const long ambientUserId = 4_242_424L;

        // A host-built context, not a fixture-built one: fixture contexts have no application service
        // provider, so UserScopingOptions resolves to null and no per-user filter is applied to them at
        // all (see BaseDbContext.ApplyUserScopingIfEnabled).
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        try
        {
            // HttpContextAccessor's backing store is a static AsyncLocal, so this is the ambient
            // principal for whichever ILoggedUserService instance the model captured, exactly as it
            // would be inside a request.
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, ambientUserId.ToString())], "Test"))
            };

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queryString = db.Set<ActivityRole>().ToQueryString();

            // ToQueryString prefixes the statement with one "-- @name='value'" line per parameter. Those
            // lines quote the id by design; the literal-versus-parameter question is about the statement
            // itself, so assert against the statement alone.
            var statement = string.Join(
                Environment.NewLine,
                queryString.Split('\n').Where(line => !line.TrimStart().StartsWith("--")));

            statement.Should().Contain("user_id",
                $"the entity must be user-scoped at all.\nQuery:\n{queryString}");
            statement.Should().NotContain(ambientUserId.ToString(),
                "the current user's id must reach the database as a parameter -- inlined as a literal it " +
                $"is cached with the compiled query and every later caller is scoped to this user.\nQuery:\n{queryString}");
            statement.Should().MatchRegex(@"user_id\s*=\s*@",
                $"the user_id comparison must be against a parameter placeholder.\nQuery:\n{queryString}");
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    /// <summary>
    /// The other hand-written per-user filter in the solution: <c>WebExtensionActivityEntry</c> is
    /// excluded from <c>ApplyUserQueryFilters</c> and gets a combined date + user filter written by hand
    /// in <c>AppDbContext.OnModelCreating</c>. It reads <c>loggedUserService</c> and
    /// <c>CurrentPartitionDate</c> off the context (both are members of <c>AppDbContext</c>), so EF
    /// re-evaluates them per execution — this test is what says so rather than assuming it. Both halves
    /// matter: a folded user id leaks browsing history across users, and a folded partition date freezes
    /// the two-year cutoff at whenever the process happened to compile the query.
    /// </summary>
    [Fact]
    public void HandWrittenWebExtensionFilter_AlsoParameterizesTheUserId()
    {
        const long ambientUserId = 5_353_535L;

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        try
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, ambientUserId.ToString())], "Test"))
            };

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queryString = db.Set<WebExtensionActivityEntry>().ToQueryString();
            var statement = string.Join(
                Environment.NewLine,
                queryString.Split('\n').Where(line => !line.TrimStart().StartsWith("--")));

            statement.Should().NotContain(ambientUserId.ToString(),
                $"the user half must reach the database as a parameter.\nQuery:\n{queryString}");
            statement.Should().MatchRegex(@"user_id\s*=\s*@",
                $"the user_id comparison must be against a parameter placeholder.\nQuery:\n{queryString}");
            statement.Should().MatchRegex(@"record_date\s*>=\s*@",
                "the partition-date bound must be a parameter too -- CurrentPartitionDate is UtcNow-2y, so " +
                $"a folded literal would freeze the cutoff for the life of the process.\nQuery:\n{queryString}");
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    /// <summary>
    /// The behavioural half: two real users, one process, one shared query shape. Order-independent —
    /// whichever of the two compiled the shape first (or some earlier test, as user 999), a baked
    /// literal means at least one of these two assertions sees the wrong rows.
    /// </summary>
    [Fact]
    public async Task TwoUsers_ReadingTheSameEndpoint_EachSeeOnlyTheirOwnRows()
    {
        var userIdA = await CreateUserAsync("scoping-a@test.com");
        var userIdB = await CreateUserAsync("scoping-b@test.com");

        await using (var db = CreateDbContext())
        {
            db.Set<ActivityRole>().AddRange(
                new ActivityRole { UserId = userIdA, Name = "role-of-a", Color = "#111111" },
                new ActivityRole { UserId = userIdB, Name = "role-of-b", Color = "#222222" });
            await db.SaveChangesAsync(CancellationToken);
        }

        var namesSeenByA = await GetRoleNamesAsync(userIdA);
        var namesSeenByB = await GetRoleNamesAsync(userIdB);

        namesSeenByA.Should().Contain("role-of-a").And.NotContain("role-of-b",
            "user A must see only their own rows");
        namesSeenByB.Should().Contain("role-of-b").And.NotContain("role-of-a",
            "user B reads the same query shape A just compiled -- if the filter baked A's id into the " +
            "cached SQL, B reads A's rows instead of their own");
    }

    private async Task<List<string>> GetRoleNamesAsync(long userId)
    {
        // GetAllActivityRoleEndpoint is a bare BaseGetAllEndpoint with no Filter() override, so the
        // global query filter is the only thing scoping it -- which is the point.
        await using var factory = CreateFactory(TestRoles.User, userId);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/activity-role", CancellationToken);
        response.EnsureSuccessStatusCode();

        var roles = await response.Content.ReadFromJsonAsync<List<RoleRow>>(JsonOpts, CancellationToken);
        return roles!.Select(r => r.Name).ToList();
    }

    private async Task<long> CreateUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
            return existing.Id;

        var user = new User
        {
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            EmailConfirmed = true,
            Locale = AvailableLocales.En,
            Timezone = TimeZoneInfo.Utc
        };
        var result = await userManager.CreateAsync(user, Password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    private sealed record RoleRow(long Id, string Name);
}
