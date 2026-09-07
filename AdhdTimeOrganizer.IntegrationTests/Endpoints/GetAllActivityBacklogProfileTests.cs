using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-1 — <c>GET /api/activity-backlog-profile</c>, the one profile route that is not a grid.
///
/// <para>Two things about it fail silently. First ownership: <c>BaseGetAllEndpoint</c> has no
/// <c>ApplyUserScoping</c> hook (unlike <c>BaseGridEndpoint</c>) — its <c>Filter</c> is the identity and the
/// handler reads the unfiltered <c>DbSet</c>, so deleting the endpoint's <c>Filter</c> override puts every
/// user's backlog profiles back in the response with no error, no log line and the same 200. Second the
/// projection: this route deliberately uses the plain <c>Projection</c>, so <c>isAnchored</c>/
/// <c>memoryAnchorId</c> are always <c>false</c>/<c>null</c> here even when the grid says otherwise, and
/// nothing records that today. Both are row-level facts, so every assertion below is on the id set or on a
/// field value, never on the status code alone.</para>
/// </summary>
[Collection("Postgres")]
public class GetAllActivityBacklogProfileTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Route = "api/activity-backlog-profile";

    private static long[] IdsOf(JsonElement[] items) => items.Select(i => i.GetProperty("id").GetInt64()).ToArray();

    private static JsonElement ItemWithId(JsonElement[] items, long id) =>
        items.Single(i => i.GetProperty("id").GetInt64() == id);

    private static long? MemoryAnchorIdOf(JsonElement item) =>
        item.GetProperty("memoryAnchorId").ValueKind == JsonValueKind.Null ? null : item.GetProperty("memoryAnchorId").GetInt64();

    private async Task<JsonElement[]> GetAllAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts, CancellationToken))!;
    }

    [Fact(DisplayName = "SEC-1: GET /activity-backlog-profile returns the caller's rows and not another user's")]
    public async Task GetAll_AsTheDefaultUser_ExcludesTheOtherUsersProfile()
    {
        var userIdB = await ActivityProfileTestSeedHelper.CreateSecondUserAsync(Fixture, "getall-backlog-a@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "A's get-all backlog");
        var (_, profileIdB) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, userIdB, "B's get-all backlog");

        var ids = IdsOf(await GetAllAsync(CreateClient()));

        // The id set, not the count: a count assertion passes whenever the fixture happens to be empty.
        ids.Should().Contain(profileIdA);
        ids.Should().NotContain(profileIdB, "the profiles are not IEntityWithUser -- this route's Filter override is the guard");
    }

    [Fact(DisplayName = "SEC-1: the same route scopes the other way round too, for the second user")]
    public async Task GetAll_AsTheSecondUser_ExcludesTheDefaultUsersProfile()
    {
        var userIdB = await ActivityProfileTestSeedHelper.CreateSecondUserAsync(Fixture, "getall-backlog-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "A's mirrored backlog");
        var (_, profileIdB) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, userIdB, "B's mirrored backlog");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var ids = IdsOf(await GetAllAsync(factoryB.CreateClient()));

        // Without this direction a "fix" that hard-codes one user id still passes the test above.
        ids.Should().Contain(profileIdB);
        ids.Should().NotContain(profileIdA);
    }

    [Fact(DisplayName = "Deliberate: GET /activity-backlog-profile always reports isAnchored=false, even for a profile the grid reports as anchored")]
    public async Task GetAll_LeavesTheCompletionFieldsAtTheirDefaults_WhileTheGridComputesThem()
    {
        await using var db = CreateDbContext();
        var (anchoredActivityId, anchoredProfileId) =
            await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Visit the planetarium");
        var anchorId = await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, anchoredActivityId, 2026, 2);

        var client = CreateClient();
        var items = await GetAllAsync(client);

        // This route uses the plain Projection, which cannot reach the anchors (Activity.MemoryAnchors was
        // deleted in the slice extraction and must stay deleted). See
        // AdhdTimeOrganizer.ActivityProfiles/docs/domain-map.md -> Endpoints -> backlog/: "it uses the plain
        // Projection, so its rows always say 'not done'".
        var fromGetAll = ItemWithId(items, anchoredProfileId);
        fromGetAll.GetProperty("isAnchored").GetBoolean().Should().BeFalse();
        MemoryAnchorIdOf(fromGetAll).Should().BeNull();

        // The comparison half: the same profile, through the grid's ProjectionWithAnchors, IS anchored.
        // Mirrors BucketList_Grid_ReportsAnchoredAndTheLatestAnchorId in ActivityProfileGridTests.
        var gridBody = new { useFilter = false, filter = new { }, sortBy = Array.Empty<object>(), itemsPerPage = 20, page = 1 };
        var gridResponse = await client.PostAsJsonAsync($"{Route}/grid", gridBody, JsonOpts, CancellationToken);
        gridResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var grid = (await gridResponse.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken))!;

        var fromGrid = ItemWithId(grid.Items, anchoredProfileId);
        fromGrid.GetProperty("isAnchored").GetBoolean().Should().BeTrue("the profile is one-time and its activity has an anchor");
        MemoryAnchorIdOf(fromGrid).Should().Be(anchorId);
    }

    [Fact(DisplayName = "GET /activity-backlog-profile challenges an anonymous caller")]
    public async Task GetAll_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(Route, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "GET /activity-backlog-profile is open to the User role -- it is not admin surface")]
    public async Task GetAll_WithOnlyTheUserRole_Returns200()
    {
        await using var db = CreateDbContext();
        var (_, profileId) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "User-role backlog");

        var ids = IdsOf(await GetAllAsync(CreateUserRoleClient()));

        ids.Should().Contain(profileId, "the global configurator gives this route User + Admin + Root");
    }

    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);
}
