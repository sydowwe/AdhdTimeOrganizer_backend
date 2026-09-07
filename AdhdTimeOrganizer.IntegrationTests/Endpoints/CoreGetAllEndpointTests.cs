using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Sydowwe.Framework.Testing.baseTests;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// `TEST-11` — the three Core <c>BaseGetAllEndpoint</c> routes, which had no assertion of any kind
/// behind them.
/// </summary>
/// <remarks>
/// <para>
/// The routes are <c>GET api/activity</c>, <c>api/activity-role</c> and <c>api/activity-category</c> —
/// <b>not</b> <c>…/all</c> as `01-testing.md` and the prompt file record them.
/// <c>BaseGetAllEndpoint.Configure()</c> registers <c>/{entity.Kebaberize()}</c> plus an optional
/// <c>RouteParam</c> suffix, and none of the three overrides <c>RouteParam</c>. The `/all` spelling
/// belongs to the select-option routes (<c>…/all-options</c>).
/// </para>
/// <para>
/// Each endpoint is a bare one-line subclass, so the base test classes below (route, 200, 401) are the
/// cheap half. The half that matters is <see cref="CoreGetAllScopingTests"/>: "returns every row this
/// user owns" is exactly the shape where a missing user filter is invisible, and these reads have no
/// hand-written scoping at all — they lean entirely on <c>AppDbContext</c>'s global query filter.
/// </para>
/// <para>
/// <c>GetAllActivityCategoryEndpoint</c>'s base-class subclass already exists next door in
/// <c>BaseGetAllEndpointTests.cs</c> (<c>GetAllActivityCategoryEndpointTests</c>); it is not repeated
/// here, only its scoping fact is added below.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class GetAllActivityEndpointTests(AppDbContextFixture fixture) : BaseGetAllEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity";

    // Activity is user-owned data; the endpoint does not override AllowedRoles(), so plain Users reach it.
    protected override bool IsAdminOnly => false;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var role = new ActivityRole { UserId = FakeLoggedUserService.TestUserId, Name = $"GetAll Activity Role {suffix}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var activity = new Activity { UserId = FakeLoggedUserService.TestUserId, Name = $"GetAll Activity {suffix}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return activity.Id;
    }
}

[Collection("Postgres")]
public class GetAllActivityRoleEndpointTests(AppDbContextFixture fixture) : BaseGetAllEndpointTests(fixture)
{
    protected override string EndpointUrl => "api/activity-role";

    protected override bool IsAdminOnly => false;

    protected override async Task<long> SeedEntityAsync(DbContext db)
    {
        var role = new ActivityRole
        {
            UserId = FakeLoggedUserService.TestUserId,
            Name = $"GetAll Role {Guid.NewGuid():N}",
            Color = "#112233"
        };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return role.Id;
    }
}

/// <summary>
/// `TEST-11` — the assertions the framework base does not make: user scoping on all three routes, and
/// what <c>GET api/activity</c> does with an archived row.
/// </summary>
/// <remarks>
/// The scoping tests mirror <c>TimerPresetCrudTests.GetAll_ReturnsOnlyCallersOwnPresets</c>, which is
/// the assertion the preset side has and the activity side did not. They assert on the returned ids
/// rather than on a count, because the Postgres container is shared across the collection and other
/// tests' rows are legitimately in the response.
/// </remarks>
[Collection("Postgres")]
public class CoreGetAllScopingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private async Task<long> SeedRoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        return role.Id;
    }

    private async Task<long> SeedCategoryAsync(DbContext db, long userId, string name)
    {
        var category = new ActivityCategory { UserId = userId, Name = $"{name} {Guid.NewGuid():N}", Color = "#334455" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);
        return category.Id;
    }

    private async Task<long> SeedActivityAsync(DbContext db, long userId, string name, bool isArchived = false)
    {
        var roleId = await SeedRoleAsync(db, userId, $"{name} Role");
        var activity = new Activity
        {
            UserId = userId,
            Name = $"{name} {Guid.NewGuid():N}",
            RoleId = roleId,
            IsArchived = isArchived
        };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long[]> GetIdsAsync(string url)
    {
        var response = await CreateClient().GetAsync(url, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts, CancellationToken);
        items.Should().NotBeNull();
        return items!.Select(i => i.GetProperty("id").GetInt64()).ToArray();
    }

    [Fact(DisplayName = "TEST-11: GET api/activity is scoped to the caller by the global query filter")]
    public async Task GetAllActivity_ReturnsOnlyCallersOwnRows()
    {
        await using var db = CreateDbContext();
        var mineA = await SeedActivityAsync(db, UserId, "Scoped Activity A");
        var mineB = await SeedActivityAsync(db, UserId, "Scoped Activity B");

        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"getall-activity-{Guid.NewGuid():N}@test.com");
        var foreignA = await SeedActivityAsync(db, otherUserId, "Foreign Activity A");
        var foreignB = await SeedActivityAsync(db, otherUserId, "Foreign Activity B");

        var ids = await GetIdsAsync("api/activity");

        ids.Should().Contain([mineA, mineB])
            .And.NotContain([foreignA, foreignB],
                "the endpoint has no hand-written scoping at all — if AppDbContext's per-user filter stops covering Activity, this route hands every user's activities to everyone with no error anywhere");
    }

    [Fact(DisplayName = "TEST-11: GET api/activity-role is scoped to the caller by the global query filter")]
    public async Task GetAllActivityRole_ReturnsOnlyCallersOwnRows()
    {
        await using var db = CreateDbContext();
        var mineA = await SeedRoleAsync(db, UserId, "Scoped Role A");
        var mineB = await SeedRoleAsync(db, UserId, "Scoped Role B");

        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"getall-role-{Guid.NewGuid():N}@test.com");
        var foreignA = await SeedRoleAsync(db, otherUserId, "Foreign Role A");
        var foreignB = await SeedRoleAsync(db, otherUserId, "Foreign Role B");

        var ids = await GetIdsAsync("api/activity-role");

        ids.Should().Contain([mineA, mineB]).And.NotContain([foreignA, foreignB]);
    }

    [Fact(DisplayName = "TEST-11: GET api/activity-category is scoped to the caller by the global query filter")]
    public async Task GetAllActivityCategory_ReturnsOnlyCallersOwnRows()
    {
        await using var db = CreateDbContext();
        var mineA = await SeedCategoryAsync(db, UserId, "Scoped Category A");
        var mineB = await SeedCategoryAsync(db, UserId, "Scoped Category B");

        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"getall-category-{Guid.NewGuid():N}@test.com");
        var foreignA = await SeedCategoryAsync(db, otherUserId, "Foreign Category A");
        var foreignB = await SeedCategoryAsync(db, otherUserId, "Foreign Category B");

        var ids = await GetIdsAsync("api/activity-category");

        ids.Should().Contain([mineA, mineB]).And.NotContain([foreignA, foreignB]);
    }

    /// <summary>
    /// `TEST-11` A2 — pins which side of the "only pickers exclude archived activities" line this route
    /// is on.
    /// </summary>
    /// <remarks>
    /// <c>GetAllActivityEndpoint</c> is a bare <c>BaseGetAllEndpoint</c>: it neither lifts
    /// <c>AppQueryFilters.ActivityArchived</c> nor excludes archived rows by hand, so the exclusion is
    /// the global filter's doing and is true by accident rather than by decision. Pinning it means a
    /// later <c>IgnoreQueryFilters</c> added for some other reason cannot quietly start leaking retired
    /// activities into whatever consumes this list. If the intended answer is the opposite, that is a
    /// product decision — `../../docs/domain-map.md` does not say — and this test is what forces it.
    /// </remarks>
    [Fact(DisplayName = "TEST-11: GET api/activity excludes archived activities")]
    public async Task GetAllActivity_ExcludesArchivedActivities()
    {
        await using var db = CreateDbContext();
        var activeId = await SeedActivityAsync(db, UserId, "Archival Probe Active");
        var archivedId = await SeedActivityAsync(db, UserId, "Archival Probe Archived", isArchived: true);

        var ids = await GetIdsAsync("api/activity");

        ids.Should().Contain(activeId)
            .And.NotContain(archivedId,
                "an archived activity is retired from every picker, and this route relies on the global archival filter to do it rather than saying so itself");
    }
}
