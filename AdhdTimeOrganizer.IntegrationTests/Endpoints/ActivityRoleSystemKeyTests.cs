using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.application.dto.response.activity;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the identity half of <see cref="ActivityRole"/>: the three roles the app itself files
/// quick-created activities under are found by <see cref="ActivityRole.SystemKey"/>, not by their
/// display name.
///
/// <para><b>The defect this exists to prevent.</b> The client used to resolve those roles with
/// <c>GET /activity-role/by-Name/To-do list task</c>. Nothing stopped a user renaming that role — the
/// Slovak UI has to — and the moment they did, the lookup 404'd and quick-create died in four dialogs
/// with no error anywhere: no exception, no log line, just an activity that never appeared. Every
/// assertion here is on rows and status codes rather than on the route existing, because a route that
/// resolves the wrong row is exactly the failure.</para>
/// </summary>
[Collection("Postgres")]
public class ActivityRoleSystemKeyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Password = "Test@1234!";

    /// <summary>
    /// The one that would have caught the original bug. The user renames the role through the real
    /// update endpoint — no special-casing, renaming a system role stays allowed — and the lookup still
    /// resolves it.
    /// </summary>
    [Fact]
    public async Task Lookup_StillFindsTheRole_AfterTheUserRenamesIt()
    {
        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);
        var client = CreateClient();

        var before = await client.GetFromJsonAsync<ActivityRoleResponse>(
            "api/activity-role/by-system-key/todoListTask", JsonOpts, CancellationToken);
        before!.Name.Should().Be("To-do list task");

        var rename = await client.PutAsJsonAsync($"api/activity-role/{before.Id}",
            new { Name = "Úloha zo zoznamu", Color = "#123456" }, JsonOpts, CancellationToken);
        rename.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await client.GetFromJsonAsync<ActivityRoleResponse>(
            "api/activity-role/by-system-key/todoListTask", JsonOpts, CancellationToken);

        after!.Id.Should().Be(before.Id, "the key is the identity; the name is display text the user owns");
        after.Name.Should().Be("Úloha zo zoznamu");
        after.SystemKey.Should().Be(SystemActivityRole.TodoListTask);
    }

    [Theory]
    [InlineData("routineTask", "Routine task")]
    [InlineData("todoListTask", "To-do list task")]
    [InlineData("plannerTask", "Planner task")]
    public async Task Lookup_ResolvesEachSeededKey(string key, string seededName)
    {
        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);

        var role = await CreateClient().GetFromJsonAsync<ActivityRoleResponse>(
            $"api/activity-role/by-system-key/{key}", JsonOpts, CancellationToken);

        role!.Name.Should().Be(seededName);
    }

    /// <summary>
    /// An unparseable key must be "no such role", not a 500 — and above all it must not fall through to
    /// <c>SystemKey == null</c>, which would hand back an arbitrary user-created role.
    /// </summary>
    [Fact]
    public async Task Lookup_UnknownKey_Is404_AndDoesNotMatchAnUnkeyedRole()
    {
        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);
        await using (var db = CreateDbContext())
        {
            db.Set<ActivityRole>().Add(new ActivityRole
            {
                UserId = FakeLoggedUserService.TestUserId, Name = "Something I made up", Color = "#010203"
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await CreateClient().GetAsync("api/activity-role/by-system-key/notAKey", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>An account that never got the seeded roles gets the 404 the client renders as a snackbar.</summary>
    [Fact]
    public async Task Lookup_Is404_WhenTheUserHasNoRoleCarryingThatKey()
    {
        var response = await CreateClient().GetAsync("api/activity-role/by-system-key/plannerTask", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The lookup rides the global <c>IEntityWithUser</c> query filter like every other role read. If it
    /// ever stops doing so, one user's quick-create files activities under another user's role.
    /// </summary>
    [Fact]
    public async Task Lookup_ReturnsOnlyTheCallersOwnRole()
    {
        var otherUserId = await CreateUserAsync($"system-key-other-{Guid.NewGuid():N}@test.com");
        await SeedDefaultRolesAsync(otherUserId);
        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);

        long expectedId;
        await using (var db = CreateDbContext())
        {
            expectedId = await db.Set<ActivityRole>()
                .IgnoreQueryFilters()
                .Where(r => r.UserId == FakeLoggedUserService.TestUserId && r.SystemKey == SystemActivityRole.PlannerTask)
                .Select(r => r.Id)
                .SingleAsync(CancellationToken);
        }

        var role = await CreateClient().GetFromJsonAsync<ActivityRoleResponse>(
            "api/activity-role/by-system-key/plannerTask", JsonOpts, CancellationToken);

        role!.Id.Should().Be(expectedId);
    }

    /// <summary>
    /// The wire spelling is the contract the client's <c>SystemActivityRole</c> enum is written against
    /// and doubles as its i18n sub-key, so this asserts on the raw JSON rather than on a round-trip that
    /// would happily accept any spelling.
    /// </summary>
    [Fact]
    public async Task RoleResponses_CarryTheCamelCaseKey_AndNullForUserCreatedRoles()
    {
        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);
        var client = CreateClient();

        var created = await client.PostAsJsonAsync("api/activity-role",
            new { Name = "My own role", Color = "#0f0f0f" }, JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdId = await created.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);

        var json = await client.GetStringAsync("api/activity-role", CancellationToken);
        json.Should().Contain("\"systemKey\":\"plannerTask\"")
            .And.Contain("\"systemKey\":\"todoListTask\"")
            .And.Contain("\"systemKey\":\"routineTask\"");

        var mine = await client.GetFromJsonAsync<ActivityRoleResponse>(
            $"api/activity-role/{createdId}", JsonOpts, CancellationToken);
        mine!.SystemKey.Should().BeNull("nothing in the API can attach a key to a user-created role");
    }

    /// <summary>
    /// Deleting a keyed role is refused. Nothing in the UI or the API can recreate one, so a successful
    /// delete would 404 the lookup for that account forever.
    /// </summary>
    [Fact]
    public async Task Delete_IsRefusedForASystemRole_AndTheRowSurvives()
    {
        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);
        var client = CreateClient();

        var role = await client.GetFromJsonAsync<ActivityRoleResponse>(
            "api/activity-role/by-system-key/routineTask", JsonOpts, CancellationToken);

        var response = await client.DeleteAsync($"api/activity-role/{role!.Id}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var db = CreateDbContext();
        var survivor = await db.Set<ActivityRole>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == role.Id, CancellationToken);
        survivor.Should().NotBeNull();
        survivor!.SystemKey.Should().Be(SystemActivityRole.RoutineTask);
    }

    [Fact]
    public async Task Delete_StillWorksForAUserCreatedRole()
    {
        var client = CreateClient();
        var created = await client.PostAsJsonAsync("api/activity-role",
            new { Name = "Disposable", Color = "#0f0f0f" }, JsonOpts, CancellationToken);
        var id = await created.Content.ReadFromJsonAsync<long>(JsonOpts, CancellationToken);

        var response = await client.DeleteAsync($"api/activity-role/{id}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A second setup pass over a user who renamed a keyed role must recognise it by key. Matching on
    /// the name alone would see the default as missing and insert a duplicate — leaving two rows the
    /// lookup could return, which the filtered unique index rejects outright.
    /// </summary>
    [Fact]
    public async Task ReSeeding_DoesNotDuplicateARenamedSystemRole()
    {
        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);

        await using (var db = CreateDbContext())
        {
            var role = await db.Set<ActivityRole>().IgnoreQueryFilters()
                .SingleAsync(r => r.UserId == FakeLoggedUserService.TestUserId
                                  && r.SystemKey == SystemActivityRole.RoutineTask, CancellationToken);
            role.Name = "Rutinná úloha";
            await db.SaveChangesAsync(CancellationToken);
        }

        await SeedDefaultRolesAsync(FakeLoggedUserService.TestUserId);

        await using var check = CreateDbContext();
        var roles = await check.Set<ActivityRole>().IgnoreQueryFilters()
            .Where(r => r.UserId == FakeLoggedUserService.TestUserId)
            .ToListAsync(CancellationToken);

        roles.Should().HaveCount(3);
        roles.Single(r => r.SystemKey == SystemActivityRole.RoutineTask).Name.Should().Be("Rutinná úloha");
    }

    private async Task SeedDefaultRolesAsync(long userId)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPerUserDefaultSeederManager>()
            .SeedForUserAsync("DefaultActivityRole", userId, false, CancellationToken);
    }

    private async Task<long> CreateUserAsync(string email)
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

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
}
