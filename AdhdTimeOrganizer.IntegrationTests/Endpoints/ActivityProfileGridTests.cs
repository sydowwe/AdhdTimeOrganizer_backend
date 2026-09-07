using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-5 / Section A — IDOR and uniqueness coverage for the three Activity*Profile grids. These
/// entities are NOT IEntityWithUser and carry no global query filter (see docs/domain-map.md ->
/// Invariants -> Ownership); every endpoint hand-scopes via "p.Activity.UserId == userId". This suite
/// exists to prove that scoping actually holds on every route, for all three profile kinds.
/// </summary>
[Collection("Postgres")]
public class ActivityProfileGridTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    // ---- seeding helpers -------------------------------------------------
    // The bodies live in ActivityProfileTestSeedHelper so GetAllActivityBacklogProfileTests shares them
    // rather than copying them; these stay as the call sites' names.

    private Task<long> CreateSecondUserAsync(string email) =>
        ActivityProfileTestSeedHelper.CreateSecondUserAsync(Fixture, email);

    private static Task<long> SeedActivityAsync(DbContext db, long userId, string name) =>
        ActivityProfileTestSeedHelper.SeedActivityAsync(db, userId, name);

    private static Task<long> SeedLookupAsync<TLookup>(DbContext db, long userId, string text)
        where TLookup : BaseLookupWithUser, new() =>
        ActivityProfileTestSeedHelper.SeedLookupAsync<TLookup>(db, userId, text);

    private static Task<(long activityId, long profileId)> SeedBacklogProfileAsync(DbContext db, long userId,
        string name = "Backlog Activity", bool isRepeatable = false) =>
        ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, userId, name, isRepeatable);

    private static Task<(long activityId, long profileId)> SeedBucketListProfileAsync(DbContext db, long userId,
        string name = "Bucket Activity") =>
        ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, userId, name);

    private static Task<(long activityId, long profileId)> SeedProjectProfileAsync(DbContext db, long userId,
        string name = "Project Activity") =>
        ActivityProfileTestSeedHelper.SeedProjectProfileAsync(db, userId, name);

    private static Task<long> SeedMemoryAnchorAsync(DbContext db, long userId, long activityId, int year, int month) =>
        ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, userId, activityId, year, month);

    private static object GridBody() => new { useFilter = false, filter = new { }, sortBy = Array.Empty<object>(), itemsPerPage = 20, page = 1 };

    private static object FilteredGridBody(object filter, object[]? sortBy = null, int itemsPerPage = 20) =>
        new { useFilter = true, filter, sortBy = sortBy ?? [], itemsPerPage, page = 1 };

    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    private static long[] IdsOf(GridResponse body) => body.Items.Select(i => i.GetProperty("id").GetInt64()).ToArray();

    private static JsonElement ItemWithId(GridResponse body, long id) =>
        body.Items.Single(i => i.GetProperty("id").GetInt64() == id);

    private static long? MemoryAnchorIdOf(JsonElement item) =>
        item.GetProperty("memoryAnchorId").ValueKind == JsonValueKind.Null ? null : item.GetProperty("memoryAnchorId").GetInt64();

    private const string BucketListOptionsRoute = "api/activity-bucket-list-profile/all-options";
    private const string ProjectOptionsRoute = "api/activity-project-profile/all-options";

    // ---- all-options helpers (TEST-3) ------------------------------------
    // The three /all-options routes are byte-identical overrides of BaseGetSelectOptionsEndpoint with
    // nothing shared between them, so each is pinned separately through these.

    private async Task<JsonElement[]> GetOptionsAsync(HttpClient client, string route)
    {
        var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts, TestContext.Current.CancellationToken))!;
    }

    private static long[] OptionIdsOf(JsonElement[] options) => options.Select(o => o.GetProperty("id").GetInt64()).ToArray();

    private static string[] OptionTextsOf(JsonElement[] options) => options.Select(o => o.GetProperty("text").GetString()!).ToArray();

    private static string TextOfOption(JsonElement[] options, long id) =>
        options.Single(o => o.GetProperty("id").GetInt64() == id).GetProperty("text").GetString()!;

    // ---- A: backlog ---------------------------------------------------------

    [Fact]
    public async Task Backlog_Grid_OnlyReturnsCallersOwnRows()
    {
        var userIdB = await CreateSecondUserAsync("backlog-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "A's backlog activity");
        var (_, profileIdB) = await SeedBacklogProfileAsync(db, userIdB, "B's backlog activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PostAsJsonAsync("api/activity-backlog-profile/grid", GridBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken);
        body!.Items.Select(i => i.GetProperty("id").GetInt64()).Should().Contain(profileIdB);
        body.Items.Select(i => i.GetProperty("id").GetInt64()).Should().NotContain(profileIdA);
    }

    // The unfiltered GET sibling of this grid lives in GetAllActivityBacklogProfileTests -- it needs both
    // scoping directions plus the projection and auth cases, which is a suite of its own.

    [Fact]
    public async Task Backlog_GetById_ForeignProfile_Returns404()
    {
        var userIdB = await CreateSecondUserAsync("backlog-getbyid-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().GetAsync($"api/activity-backlog-profile/{profileIdA}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Backlog_UpdateAndDelete_ForeignProfile_AreRefusedAndRowUnchanged()
    {
        var userIdB = await CreateSecondUserAsync("backlog-write-b@test.com");
        await using var db = CreateDbContext();
        var (activityIdA, profileIdA) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var clientB = factoryB.CreateClient();

        var updatePayload = new
        {
            ActivityId = activityIdA,
            LocationTypeId = 0L,
            WeatherDependencyId = 0L,
            EnergyLevel = "High",
            MinParticipants = 2,
            ExpectedCostTierId = 0L,
            DurationMinutes = 999,
            IsRepeatable = true
        };
        var updateResponse = await clientB.PutAsJsonAsync($"api/activity-backlog-profile/{profileIdA}", updatePayload, JsonOpts, TestContext.Current.CancellationToken);
        updateResponse.IsSuccessStatusCode.Should().BeFalse();

        var deleteResponse = await clientB.DeleteAsync($"api/activity-backlog-profile/{profileIdA}", TestContext.Current.CancellationToken);
        deleteResponse.IsSuccessStatusCode.Should().BeFalse();

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<ActivityBacklogProfile>().AsNoTracking().FirstAsync(p => p.Id == profileIdA, CancellationToken);
        stillThere.DurationMinutes.Should().Be(30, "the foreign write must not have taken effect");
        stillThere.MinParticipants.Should().Be(1);
    }

    [Fact]
    public async Task Backlog_SelectOptions_OnlyReturnsCallersOwnRows()
    {
        var userIdB = await CreateSecondUserAsync("backlog-options-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId);
        var (_, profileIdB) = await SeedBacklogProfileAsync(db, userIdB);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().GetAsync("api/activity-backlog-profile/all-options", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts, TestContext.Current.CancellationToken);
        items!.Select(i => i.GetProperty("id").GetInt64()).Should().Contain(profileIdB);
        items.Select(i => i.GetProperty("id").GetInt64()).Should().NotContain(profileIdA);
    }

    [Fact]
    public async Task Backlog_SecondProfileForSameActivity_IsRejected()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Already has backlog");
        var locationTypeId = await SeedLookupAsync<ActivityLocationType>(db, FakeLoggedUserService.TestUserId, "Second Home");
        var weatherId = await SeedLookupAsync<ActivityWeatherDependency>(db, FakeLoggedUserService.TestUserId, "Second Weather");
        var costTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, FakeLoggedUserService.TestUserId, "Second Cost");

        var payload = new
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherId,
            EnergyLevel = "Medium",
            MinParticipants = 1,
            ExpectedCostTierId = costTierId,
            DurationMinutes = 15,
            IsRepeatable = false
        };

        var response = await CreateClient().PostAsJsonAsync("api/activity-backlog-profile", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the create validator rejects a second profile of the same kind, not a raw 500 from the unique index");
    }

    // ---- A: bucket list -------------------------------------------------

    [Fact]
    public async Task BucketList_Grid_OnlyReturnsCallersOwnRows()
    {
        var userIdB = await CreateSecondUserAsync("bucket-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "A's bucket activity");
        var (_, profileIdB) = await SeedBucketListProfileAsync(db, userIdB, "B's bucket activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile/grid", GridBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken);
        body!.Items.Select(i => i.GetProperty("id").GetInt64()).Should().Contain(profileIdB);
        body.Items.Select(i => i.GetProperty("id").GetInt64()).Should().NotContain(profileIdA);
    }

    [Fact]
    public async Task BucketList_GetById_ForeignProfile_Returns404()
    {
        var userIdB = await CreateSecondUserAsync("bucket-getbyid-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().GetAsync($"api/activity-bucket-list-profile/{profileIdA}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BucketList_UpdateAndDelete_ForeignProfile_AreRefusedAndRowUnchanged()
    {
        var userIdB = await CreateSecondUserAsync("bucket-write-b@test.com");
        await using var db = CreateDbContext();
        var (activityIdA, profileIdA) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var clientB = factoryB.CreateClient();

        var updatePayload = new
        {
            ActivityId = activityIdA,
            ExperienceTypeId = 0L,
            ComfortZoneStep = 3,
            RequiresTravel = true,
            InspirationSource = "Hijacked"
        };
        var updateResponse = await clientB.PutAsJsonAsync($"api/activity-bucket-list-profile/{profileIdA}", updatePayload, JsonOpts, TestContext.Current.CancellationToken);
        updateResponse.IsSuccessStatusCode.Should().BeFalse();

        var deleteResponse = await clientB.DeleteAsync($"api/activity-bucket-list-profile/{profileIdA}", TestContext.Current.CancellationToken);
        deleteResponse.IsSuccessStatusCode.Should().BeFalse();

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<ActivityBucketListProfile>().AsNoTracking().FirstAsync(p => p.Id == profileIdA, CancellationToken);
        stillThere.ComfortZoneStep.Should().Be(5);
        stillThere.InspirationSource.Should().Be("A friend's story");
    }

    [Fact]
    public async Task BucketList_SecondProfileForSameActivity_IsRejected()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Already has bucket list");
        var experienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, FakeLoggedUserService.TestUserId, "Second Experience");

        var payload = new
        {
            ActivityId = activityId,
            ExperienceTypeId = experienceTypeId,
            ComfortZoneStep = 3,
            RequiresTravel = false,
            InspirationSource = "Another source"
        };

        var response = await CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "TEST-3: bucket-list /all-options returns the caller's rows and not another user's")]
    public async Task BucketList_SelectOptions_OnlyReturnsCallersOwnRows()
    {
        var userIdB = await CreateSecondUserAsync("bucket-options-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "A's optioned bucket activity");
        var (_, profileIdB) = await SeedBucketListProfileAsync(db, userIdB, "B's optioned bucket activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var ids = OptionIdsOf(await GetOptionsAsync(factoryB.CreateClient(), BucketListOptionsRoute));

        ids.Should().Contain(profileIdB);
        ids.Should().NotContain(profileIdA, "the profile is not IEntityWithUser -- this endpoint's Filter override is the only guard");
    }

    [Fact(DisplayName = "TEST-3: a bucket-list option carries the profile id with the activity's name")]
    public async Task BucketList_SelectOptions_IdIsTheProfileAndTextIsTheActivityName()
    {
        await using var db = CreateDbContext();
        var (activityId, profileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Sleep in a glass igloo");

        var options = await GetOptionsAsync(CreateClient(), BucketListOptionsRoute);

        // The client renders text into the picker and sends id back as a profile reference, so swapping the
        // two in a refactor still answers 200 with a plausible-looking body.
        TextOfOption(options, profileId).Should().Be("Sleep in a glass igloo");
        OptionIdsOf(options).Should().NotContain(activityId, "the id is the profile's, never the activity's");
    }

    [Fact(DisplayName = "TEST-3: bucket-list options come back ordered by activity name")]
    public async Task BucketList_SelectOptions_AreOrderedByActivityName()
    {
        // A fresh user, so the assertion is on the whole ordered list rather than on a subsequence of
        // whatever the shared default user accumulated.
        var userIdB = await CreateSecondUserAsync("bucket-options-order-b@test.com");
        await using var db = CreateDbContext();
        await SeedBucketListProfileAsync(db, userIdB, "Zeppelin ride");
        await SeedBucketListProfileAsync(db, userIdB, "Abseiling");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var options = await GetOptionsAsync(factoryB.CreateClient(), BucketListOptionsRoute);

        OptionTextsOf(options).Should().Equal("Abseiling", "Zeppelin ride");
    }

    [Fact(DisplayName = "TEST-3: bucket-list /all-options answers 200 with [] when the caller has no profiles")]
    public async Task BucketList_SelectOptions_WithNoRows_IsAnEmptyArrayNot404()
    {
        var userIdB = await CreateSecondUserAsync("bucket-options-empty-b@test.com");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var options = await GetOptionsAsync(factoryB.CreateClient(), BucketListOptionsRoute);

        options.Should().BeEmpty();
    }

    [Fact(DisplayName = "TEST-3: bucket-list /all-options challenges an anonymous caller")]
    public async Task BucketList_SelectOptions_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(BucketListOptionsRoute, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "TEST-3: bucket-list /all-options is open to the User role -- it is not admin surface")]
    public async Task BucketList_SelectOptions_WithOnlyTheUserRole_Returns200()
    {
        await using var db = CreateDbContext();
        var (_, profileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "User-role bucket activity");

        var ids = OptionIdsOf(await GetOptionsAsync(CreateUserRoleClient(), BucketListOptionsRoute));

        ids.Should().Contain(profileId, "the default AllowedRoles() is User + Admin + Root");
    }

    // ---- A: project -------------------------------------------------

    [Fact]
    public async Task Project_Grid_OnlyReturnsCallersOwnRows()
    {
        var userIdB = await CreateSecondUserAsync("project-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, "A's project activity");
        var (_, profileIdB) = await SeedProjectProfileAsync(db, userIdB, "B's project activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PostAsJsonAsync("api/activity-project-profile/grid", GridBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken);
        body!.Items.Select(i => i.GetProperty("id").GetInt64()).Should().Contain(profileIdB);
        body.Items.Select(i => i.GetProperty("id").GetInt64()).Should().NotContain(profileIdA);
    }

    [Fact]
    public async Task Project_GetById_ForeignProfile_Returns404()
    {
        var userIdB = await CreateSecondUserAsync("project-getbyid-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().GetAsync($"api/activity-project-profile/{profileIdA}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Project_UpdateAndDelete_ForeignProfile_AreRefusedAndRowUnchanged()
    {
        var userIdB = await CreateSecondUserAsync("project-write-b@test.com");
        await using var db = CreateDbContext();
        var (activityIdA, profileIdA) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var clientB = factoryB.CreateClient();

        var updatePayload = new
        {
            ActivityId = activityIdA,
            DifficultyLevel = "Expert",
            ProjectArea = "Hijacked",
            EstimatedHours = 100,
            IsMessy = true,
            MaterialsNeeded = Array.Empty<string>(),
            RequiredTools = Array.Empty<string>(),
            ReadinessStatus = "ReadyToStart"
        };
        var updateResponse = await clientB.PutAsJsonAsync($"api/activity-project-profile/{profileIdA}", updatePayload, JsonOpts, TestContext.Current.CancellationToken);
        updateResponse.IsSuccessStatusCode.Should().BeFalse();

        var deleteResponse = await clientB.DeleteAsync($"api/activity-project-profile/{profileIdA}", TestContext.Current.CancellationToken);
        deleteResponse.IsSuccessStatusCode.Should().BeFalse();

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<ActivityProjectProfile>().AsNoTracking().FirstAsync(p => p.Id == profileIdA, CancellationToken);
        stillThere.ProjectArea.Should().Be("Woodworking");
        stillThere.DifficultyLevel.Should().Be(DifficultyLevel.Beginner);
    }

    [Fact]
    public async Task Project_PatchStatus_ChangesOnlyReadinessStatus()
    {
        await using var db = CreateDbContext();
        var (_, profileId) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, "Patchable project activity");

        var response = await CreateClient().PatchAsJsonAsync(
            $"api/activity-project-profile/{profileId}/status",
            new { ReadinessStatus = "ReadyToStart" },
            JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var patched = await assertDb.Set<ActivityProjectProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
        patched.ReadinessStatus.Should().Be(ReadinessStatus.ReadyToStart);
        patched.ProjectArea.Should().Be("Woodworking", "a status-only patch must not touch the other columns");
        patched.DifficultyLevel.Should().Be(DifficultyLevel.Beginner);
        patched.EstimatedHours.Should().Be(4);
    }

    [Fact]
    public async Task Project_PatchStatus_ForeignProfile_IsRefusedAndRowUnchanged()
    {
        var userIdB = await CreateSecondUserAsync("project-patch-status-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PatchAsJsonAsync(
            $"api/activity-project-profile/{profileIdA}/status",
            new { ReadinessStatus = "ReadyToStart" },
            JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<ActivityProjectProfile>().AsNoTracking().FirstAsync(p => p.Id == profileIdA, CancellationToken);
        stillThere.ReadinessStatus.Should().Be(ReadinessStatus.Planning, "the profile has no global user filter -- this hand-scoped check is the only guard");
    }

    [Fact]
    public async Task Project_SecondProfileForSameActivity_IsRejected()
    {
        await using var db = CreateDbContext();
        var (activityId, _) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, "Already has project profile");

        var payload = new
        {
            ActivityId = activityId,
            DifficultyLevel = "Beginner",
            ProjectArea = "Another area",
            EstimatedHours = 2,
            IsMessy = false,
            MaterialsNeeded = Array.Empty<string>(),
            RequiredTools = Array.Empty<string>(),
            ReadinessStatus = "Planning"
        };

        var response = await CreateClient().PostAsJsonAsync("api/activity-project-profile", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "TEST-3: project /all-options returns the caller's rows and not another user's")]
    public async Task Project_SelectOptions_OnlyReturnsCallersOwnRows()
    {
        var userIdB = await CreateSecondUserAsync("project-options-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, "A's optioned project activity");
        var (_, profileIdB) = await SeedProjectProfileAsync(db, userIdB, "B's optioned project activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var ids = OptionIdsOf(await GetOptionsAsync(factoryB.CreateClient(), ProjectOptionsRoute));

        ids.Should().Contain(profileIdB);
        ids.Should().NotContain(profileIdA, "the profile is not IEntityWithUser -- this endpoint's Filter override is the only guard");
    }

    [Fact(DisplayName = "TEST-3: a project option carries the profile id with the activity's name")]
    public async Task Project_SelectOptions_IdIsTheProfileAndTextIsTheActivityName()
    {
        await using var db = CreateDbContext();
        var (activityId, profileId) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, "Build a bookshelf");

        var options = await GetOptionsAsync(CreateClient(), ProjectOptionsRoute);

        TextOfOption(options, profileId).Should().Be("Build a bookshelf");
        OptionIdsOf(options).Should().NotContain(activityId, "the id is the profile's, never the activity's");
    }

    [Fact(DisplayName = "TEST-3: project options come back ordered by activity name")]
    public async Task Project_SelectOptions_AreOrderedByActivityName()
    {
        var userIdB = await CreateSecondUserAsync("project-options-order-b@test.com");
        await using var db = CreateDbContext();
        await SeedProjectProfileAsync(db, userIdB, "Zeppelin ride");
        await SeedProjectProfileAsync(db, userIdB, "Abseiling");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var options = await GetOptionsAsync(factoryB.CreateClient(), ProjectOptionsRoute);

        OptionTextsOf(options).Should().Equal("Abseiling", "Zeppelin ride");
    }

    [Fact(DisplayName = "TEST-3: project /all-options answers 200 with [] when the caller has no profiles")]
    public async Task Project_SelectOptions_WithNoRows_IsAnEmptyArrayNot404()
    {
        var userIdB = await CreateSecondUserAsync("project-options-empty-b@test.com");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var options = await GetOptionsAsync(factoryB.CreateClient(), ProjectOptionsRoute);

        options.Should().BeEmpty();
    }

    [Fact(DisplayName = "TEST-3: project /all-options challenges an anonymous caller")]
    public async Task Project_SelectOptions_Unauthenticated_Returns401()
    {
        var response = await CreateUnauthenticatedClient().GetAsync(ProjectOptionsRoute, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "TEST-3: project /all-options is open to the User role -- it is not admin surface")]
    public async Task Project_SelectOptions_WithOnlyTheUserRole_Returns200()
    {
        await using var db = CreateDbContext();
        var (_, profileId) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, "User-role project activity");

        var ids = OptionIdsOf(await GetOptionsAsync(CreateUserRoleClient(), ProjectOptionsRoute));

        ids.Should().Contain(profileId, "the default AllowedRoles() is User + Admin + Root");
    }

    // ---- B1: completion (isAnchored / memoryAnchorId) ---------------------------------------------
    //
    // "Done" on the bucket list and the one-time backlog is derived, not stored: a MemoryAnchor against
    // the profile's Activity IS the completion. Nothing in the schema records that, so these are the only
    // tests that would notice the derivation drifting -- the grid keeps answering 200 either way.

    [Fact]
    public async Task BucketList_Grid_ReportsAnchoredAndTheLatestAnchorId()
    {
        await using var db = CreateDbContext();
        var (doneActivityId, doneProfileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Skydiving");
        var (_, todoProfileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Northern lights");

        await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, doneActivityId, 2025, 6);
        var latestAnchorId = await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, doneActivityId, 2026, 2);

        var response = await CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile/grid", GridBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!;

        var done = ItemWithId(body, doneProfileId);
        done.GetProperty("isAnchored").GetBoolean().Should().BeTrue();
        MemoryAnchorIdOf(done).Should().Be(latestAnchorId, "the most recent anchor by (year, month, id) is the one the chip links to");

        var todo = ItemWithId(body, todoProfileId);
        todo.GetProperty("isAnchored").GetBoolean().Should().BeFalse();
        MemoryAnchorIdOf(todo).Should().BeNull();
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    public async Task BucketList_Grid_IsAnchoredFilter_IsTriState(bool? isAnchored, int expectedCount)
    {
        await using var db = CreateDbContext();
        var (doneActivityId, doneProfileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Skydiving");
        var (_, todoProfileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Northern lights");
        await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, doneActivityId, 2026, 2);

        // itemsPerPage 1 on purpose: this is the shape the "n of m experienced" readout sends, and it reads
        // only itemsCount -- which is counted before pagination and must not be capped by the page size.
        var response = await CreateClient().PostAsJsonAsync(
            "api/activity-bucket-list-profile/grid",
            FilteredGridBody(new { isAnchored }, itemsPerPage: 1),
            JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!;
        body.ItemsCount.Should().Be(expectedCount);

        if (isAnchored == true)
            IdsOf(body).Should().Equal(doneProfileId);
        if (isAnchored == false)
            IdsOf(body).Should().Equal(todoProfileId);
    }

    [Fact]
    public async Task BucketList_Grid_SortsByIsAnchored()
    {
        await using var db = CreateDbContext();
        var (doneActivityId, doneProfileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Skydiving");
        var (_, todoProfileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Northern lights");
        await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, doneActivityId, 2026, 2);

        var client = CreateClient();

        var ascending = await client.PostAsJsonAsync(
            "api/activity-bucket-list-profile/grid",
            FilteredGridBody(new { }, [new { key = "isAnchored", isDesc = false }]),
            JsonOpts, TestContext.Current.CancellationToken);

        // isAnchored is computed in the projection, not stored -- if it were overlaid after the query the
        // sort would silently run on `false` for every row and this would pass by accident half the time.
        ascending.StatusCode.Should().Be(HttpStatusCode.OK);
        IdsOf((await ascending.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!)
            .Should().Equal([todoProfileId, doneProfileId], "ascending puts the entries still to do first");

        var descending = await client.PostAsJsonAsync(
            "api/activity-bucket-list-profile/grid",
            FilteredGridBody(new { }, [new { key = "isAnchored", isDesc = true }]),
            JsonOpts, TestContext.Current.CancellationToken);

        descending.StatusCode.Should().Be(HttpStatusCode.OK);
        IdsOf((await descending.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!)
            .Should().Equal(doneProfileId, todoProfileId);
    }

    [Fact]
    public async Task Backlog_Grid_RepeatableEntry_IsNeverAnchored()
    {
        await using var db = CreateDbContext();
        var (onceActivityId, onceProfileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "See a solar eclipse");
        var (repeatActivityId, repeatProfileId) =
            await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Evening walk", isRepeatable: true);

        var onceAnchorId = await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, onceActivityId, 2026, 3);
        await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, repeatActivityId, 2026, 3);

        var response = await CreateClient().PostAsJsonAsync("api/activity-backlog-profile/grid", GridBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!;

        var once = ItemWithId(body, onceProfileId);
        once.GetProperty("isAnchored").GetBoolean().Should().BeTrue();
        MemoryAnchorIdOf(once).Should().Be(onceAnchorId);

        var repeat = ItemWithId(body, repeatProfileId);
        repeat.GetProperty("isAnchored").GetBoolean()
            .Should().BeFalse("a repeatable entry is never finished, however many anchors its activity carries");
        MemoryAnchorIdOf(repeat).Should().BeNull();
    }

    [Fact]
    public async Task Backlog_Grid_IsOneTimeAndIsAnchoredFiltersCompose()
    {
        await using var db = CreateDbContext();
        var (doneActivityId, doneProfileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "See a solar eclipse");
        var (_, todoProfileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Learn to solder");
        var (repeatActivityId, _) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Evening walk", isRepeatable: true);
        await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, doneActivityId, 2026, 3);
        await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, repeatActivityId, 2026, 3);

        var client = CreateClient();

        // The two requests behind "1 of 2 experienced": the repeatable entry is not part of either total.
        var denominator = await client.PostAsJsonAsync(
            "api/activity-backlog-profile/grid",
            FilteredGridBody(new { isOneTime = true }, itemsPerPage: 1),
            JsonOpts, TestContext.Current.CancellationToken);
        denominator.StatusCode.Should().Be(HttpStatusCode.OK);
        (await denominator.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!.ItemsCount.Should().Be(2);

        var numerator = await client.PostAsJsonAsync(
            "api/activity-backlog-profile/grid",
            FilteredGridBody(new { isOneTime = true, isAnchored = true }, itemsPerPage: 1),
            JsonOpts, TestContext.Current.CancellationToken);
        numerator.StatusCode.Should().Be(HttpStatusCode.OK);
        var numeratorBody = (await numerator.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!;
        numeratorBody.ItemsCount.Should().Be(1);
        IdsOf(numeratorBody).Should().Equal(doneProfileId);

        // isAnchored:false must agree with the response field, so the repeatable entry lands here too.
        var notYet = await client.PostAsJsonAsync(
            "api/activity-backlog-profile/grid",
            FilteredGridBody(new { isAnchored = false }),
            JsonOpts, TestContext.Current.CancellationToken);
        notYet.StatusCode.Should().Be(HttpStatusCode.OK);
        var notYetBody = (await notYet.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!;
        IdsOf(notYetBody).Should().Contain(todoProfileId).And.NotContain(doneProfileId);
        notYetBody.ItemsCount.Should().Be(2, "the repeatable entry reports isAnchored: false and must be counted as such");
    }

    [Fact]
    public async Task BucketList_Grid_AnotherUsersAnchorDoesNotMarkTheEntryDone()
    {
        var userIdB = await CreateSecondUserAsync("anchor-scope-b@test.com");
        await using var db = CreateDbContext();
        var (activityIdA, profileIdA) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Skydiving");

        // Seeded straight through the DbContext: the anchor endpoints would refuse this, and that refusal is
        // exactly what must NOT be the only thing standing between B's row and A's completion column.
        await SeedMemoryAnchorAsync(db, userIdB, activityIdA, 2026, 2);

        var response = await CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile/grid", GridBody(), JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, TestContext.Current.CancellationToken))!;
        var item = ItemWithId(body, profileIdA);
        item.GetProperty("isAnchored").GetBoolean().Should().BeFalse("the anchor subquery is scoped to the caller");
        MemoryAnchorIdOf(item).Should().BeNull();
    }

    [Fact]
    public async Task BucketList_GetById_ReportsTheSameCompletionAsTheGrid()
    {
        await using var db = CreateDbContext();
        var (activityId, profileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Skydiving");
        var anchorId = await SeedMemoryAnchorAsync(db, FakeLoggedUserService.TestUserId, activityId, 2026, 2);

        var response = await CreateClient().GetAsync($"api/activity-bucket-list-profile/{profileId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, TestContext.Current.CancellationToken);
        item.GetProperty("isAnchored").GetBoolean()
            .Should().BeTrue("GetById projects in memory and cannot reach the anchors -- it must overlay them, not answer false");
        item.GetProperty("memoryAnchorId").GetInt64().Should().Be(anchorId);
    }

    // ---- Lookup ownership on the write surface ---------------------------------------------------
    //
    // The four Activity* lookups are per-user, but a profile stores them as raw long FK columns and is
    // not IEntityWithUser. The FK proves the row exists, not that it is the caller's, so before these
    // guards a create/update naming a stranger's sequential id inserted cleanly -- and read back with a
    // null label, since the navigation is user-filtered. Row assertions, because both halves answer
    // 400/201 the same way whether or not the check is there.

    [Fact]
    public async Task Backlog_Create_WithAnotherUsersLookupIds_IsRejectedAndWritesNothing()
    {
        var userIdB = await CreateSecondUserAsync("backlog-lookup-create-b@test.com");
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, FakeLoggedUserService.TestUserId, "Wants a backlog profile");
        var foreignLocationTypeId = await SeedLookupAsync<ActivityLocationType>(db, userIdB, "B's Home");
        var foreignWeatherId = await SeedLookupAsync<ActivityWeatherDependency>(db, userIdB, "B's Indoor");
        var foreignCostTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, userIdB, "B's Free");

        var payload = new
        {
            ActivityId = activityId,
            LocationTypeId = foreignLocationTypeId,
            WeatherDependencyId = foreignWeatherId,
            EnergyLevel = "Medium",
            MinParticipants = 1,
            ExpectedCostTierId = foreignCostTierId,
            DurationMinutes = 15,
            IsRepeatable = false
        };

        var response = await CreateClient().PostAsJsonAsync("api/activity-backlog-profile", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var assertDb = CreateDbContext();
        var written = await assertDb.Set<ActivityBacklogProfile>().AsNoTracking().AnyAsync(p => p.ActivityId == activityId, CancellationToken);
        written.Should().BeFalse("the row would pin three lookups its owner could then never delete -- all three FKs are Restrict");
    }

    [Fact]
    public async Task Backlog_Create_WithOwnLookupIds_Succeeds()
    {
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, FakeLoggedUserService.TestUserId, "Wants a backlog profile too");
        var locationTypeId = await SeedLookupAsync<ActivityLocationType>(db, FakeLoggedUserService.TestUserId, "Own Home");
        var weatherId = await SeedLookupAsync<ActivityWeatherDependency>(db, FakeLoggedUserService.TestUserId, "Own Indoor");
        var costTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, FakeLoggedUserService.TestUserId, "Own Free");

        var payload = new
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherId,
            EnergyLevel = "Medium",
            MinParticipants = 1,
            ExpectedCostTierId = costTierId,
            DurationMinutes = 15,
            IsRepeatable = false
        };

        var response = await CreateClient().PostAsJsonAsync("api/activity-backlog-profile", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the ownership check must not degrade into a blanket rejection");
    }

    [Fact]
    public async Task Backlog_Update_WithAnotherUsersLookupIds_IsRejectedAndRowUnchanged()
    {
        var userIdB = await CreateSecondUserAsync("backlog-lookup-update-b@test.com");
        await using var db = CreateDbContext();
        var (activityId, profileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Owned backlog activity");
        var ownLocationTypeId = await SeedLookupAsync<ActivityLocationType>(db, FakeLoggedUserService.TestUserId, "Own update Home");
        var ownCostTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, FakeLoggedUserService.TestUserId, "Own update Free");
        var foreignWeatherId = await SeedLookupAsync<ActivityWeatherDependency>(db, userIdB, "B's update Indoor");

        var payload = new
        {
            ActivityId = activityId,
            LocationTypeId = ownLocationTypeId,
            WeatherDependencyId = foreignWeatherId,
            EnergyLevel = "High",
            MinParticipants = 2,
            ExpectedCostTierId = ownCostTierId,
            DurationMinutes = 999,
            IsRepeatable = true
        };

        var response = await CreateClient().PutAsJsonAsync($"api/activity-backlog-profile/{profileId}", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "UpdateEntity rewrites all three FKs, so update is as much of a write surface for them as create -- one foreign id out of three is enough");

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<ActivityBacklogProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
        stillThere.WeatherDependencyId.Should().NotBe(foreignWeatherId);
        stillThere.LocationTypeId.Should().NotBe(ownLocationTypeId, "a rejected update writes nothing at all, not just the offending column");
        stillThere.DurationMinutes.Should().Be(30);
    }

    [Fact]
    public async Task BucketList_Create_WithAnotherUsersExperienceType_IsRejectedAndWritesNothing()
    {
        var userIdB = await CreateSecondUserAsync("bucket-lookup-create-b@test.com");
        await using var db = CreateDbContext();
        var activityId = await SeedActivityAsync(db, FakeLoggedUserService.TestUserId, "Wants a bucket list profile");
        var foreignExperienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, userIdB, "B's Adventure");

        var payload = new
        {
            ActivityId = activityId,
            ExperienceTypeId = foreignExperienceTypeId,
            ComfortZoneStep = 3,
            RequiresTravel = false,
            InspirationSource = "Someone else's list"
        };

        var response = await CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var assertDb = CreateDbContext();
        var written = await assertDb.Set<ActivityBucketListProfile>().AsNoTracking().AnyAsync(p => p.ActivityId == activityId, CancellationToken);
        written.Should().BeFalse();
    }

    [Fact]
    public async Task BucketList_Update_WithAnotherUsersExperienceType_IsRejectedAndRowUnchanged()
    {
        var userIdB = await CreateSecondUserAsync("bucket-lookup-update-b@test.com");
        await using var db = CreateDbContext();
        var (activityId, profileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Owned bucket activity");
        var foreignExperienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, userIdB, "B's update Adventure");

        var payload = new
        {
            ActivityId = activityId,
            ExperienceTypeId = foreignExperienceTypeId,
            ComfortZoneStep = 3,
            RequiresTravel = true,
            InspirationSource = "Repointed"
        };

        var response = await CreateClient().PutAsJsonAsync($"api/activity-bucket-list-profile/{profileId}", payload, JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var assertDb = CreateDbContext();
        var stillThere = await assertDb.Set<ActivityBucketListProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
        stillThere.ExperienceTypeId.Should().NotBe(foreignExperienceTypeId);
        stillThere.ComfortZoneStep.Should().Be(5);
    }

    [Fact]
    public async Task BucketList_Update_WithOwnExperienceType_Succeeds()
    {
        await using var db = CreateDbContext();
        var (activityId, profileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Editable bucket activity");
        var ownExperienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, FakeLoggedUserService.TestUserId, "Own Adventure");

        var payload = new
        {
            ActivityId = activityId,
            ExperienceTypeId = ownExperienceTypeId,
            ComfortZoneStep = 4,
            RequiresTravel = true,
            InspirationSource = "A podcast"
        };

        var response = await CreateClient().PutAsJsonAsync($"api/activity-bucket-list-profile/{profileId}", payload, JsonOpts, TestContext.Current.CancellationToken);

        // 200 with the id, not 204: the update runs on BaseUpdateEndpoint like the rest of the family.
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the ownership check must not degrade into a blanket rejection");

        await using var assertDb = CreateDbContext();
        var updated = await assertDb.Set<ActivityBucketListProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
        updated.ExperienceTypeId.Should().Be(ownExperienceTypeId);
    }

    /// <summary>
    /// The comfort-zone range is 1–5 on both write paths. Nothing in the schema says so — the column is a
    /// plain <c>int</c> — and a 6+ row does not throw anywhere either: it just scores negatively in
    /// <c>LeisureDrawRanker.Score</c> (<c>(6 - step)</c>) and quietly sinks below every other candidate in
    /// the draw. Only the validators stand between the two, so only a rejected write notices them going.
    /// </summary>
    [Fact]
    public async Task BucketList_ComfortZoneStepAboveFive_IsRejectedOnBothWritePaths()
    {
        await using var db = CreateDbContext();
        var (activityId, profileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "Comfort range activity");
        var freshActivityId = await SeedActivityAsync(db, FakeLoggedUserService.TestUserId, "Wants an out-of-range profile");
        var experienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, FakeLoggedUserService.TestUserId, "Range Adventure");

        object Payload(long forActivityId, int step) => new
        {
            ActivityId = forActivityId,
            ExperienceTypeId = experienceTypeId,
            ComfortZoneStep = step,
            RequiresTravel = false,
            InspirationSource = "Out of range"
        };

        var client = CreateClient();

        var createResponse = await client.PostAsJsonAsync("api/activity-bucket-list-profile", Payload(freshActivityId, 6), JsonOpts, TestContext.Current.CancellationToken);
        createResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var updateResponse = await client.PutAsJsonAsync($"api/activity-bucket-list-profile/{profileId}", Payload(activityId, 6), JsonOpts, TestContext.Current.CancellationToken);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var assertDb = CreateDbContext();
        var created = await assertDb.Set<ActivityBucketListProfile>().AsNoTracking().AnyAsync(p => p.ActivityId == freshActivityId, CancellationToken);
        created.Should().BeFalse();
        var stillThere = await assertDb.Set<ActivityBucketListProfile>().AsNoTracking().FirstAsync(p => p.Id == profileId, CancellationToken);
        stillThere.ComfortZoneStep.Should().Be(5, "the top of the range is still a valid stored value");
    }

    // ---- Batch delete: ownership holds per id, and one foreign id voids the whole batch ----------
    //
    // The batch routes are the one write surface the rest of this suite did not reach. They answer 204 on
    // success either way, so only a row assertion notices the per-id ownership check going missing.

    [Fact]
    public async Task Backlog_BatchDelete_WithAForeignId_DeletesNothing()
    {
        var userIdB = await CreateSecondUserAsync("backlog-batch-b@test.com");
        await using var db = CreateDbContext();
        var (_, ownProfileId) = await SeedBacklogProfileAsync(db, userIdB, "B's batch backlog activity");
        var (_, foreignProfileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "A's batch backlog activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PostAsJsonAsync(
            "api/activity-backlog-profile/batch-delete",
            new { Ids = new[] { ownProfileId, foreignProfileId } },
            JsonOpts, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();

        await using var assertDb = CreateDbContext();
        var surviving = await assertDb.Set<ActivityBacklogProfile>().AsNoTracking()
            .CountAsync(p => p.Id == ownProfileId || p.Id == foreignProfileId, CancellationToken);
        surviving.Should().Be(2, "a batch carrying an id that is not the caller's must delete nothing at all");
    }

    [Fact]
    public async Task BucketList_BatchDelete_WithAForeignId_DeletesNothing()
    {
        var userIdB = await CreateSecondUserAsync("bucket-batch-b@test.com");
        await using var db = CreateDbContext();
        var (_, ownProfileId) = await SeedBucketListProfileAsync(db, userIdB, "B's batch bucket activity");
        var (_, foreignProfileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId, "A's batch bucket activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PostAsJsonAsync(
            "api/activity-bucket-list-profile/batch-delete",
            new { Ids = new[] { ownProfileId, foreignProfileId } },
            JsonOpts, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();

        await using var assertDb = CreateDbContext();
        var surviving = await assertDb.Set<ActivityBucketListProfile>().AsNoTracking()
            .CountAsync(p => p.Id == ownProfileId || p.Id == foreignProfileId, CancellationToken);
        surviving.Should().Be(2);
    }

    [Fact]
    public async Task Project_BatchDelete_WithAForeignId_DeletesNothing()
    {
        var userIdB = await CreateSecondUserAsync("project-batch-b@test.com");
        await using var db = CreateDbContext();
        var (_, ownProfileId) = await SeedProjectProfileAsync(db, userIdB, "B's batch project activity");
        var (_, foreignProfileId) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId, "A's batch project activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PostAsJsonAsync(
            "api/activity-project-profile/batch-delete",
            new { Ids = new[] { ownProfileId, foreignProfileId } },
            JsonOpts, TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();

        await using var assertDb = CreateDbContext();
        var surviving = await assertDb.Set<ActivityProjectProfile>().AsNoTracking()
            .CountAsync(p => p.Id == ownProfileId || p.Id == foreignProfileId, CancellationToken);
        surviving.Should().Be(2);
    }

    [Fact]
    public async Task Backlog_BatchDelete_OfOwnRows_RemovesThemAll()
    {
        await using var db = CreateDbContext();
        var (_, firstProfileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "First batch-deletable activity");
        var (_, secondProfileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "Second batch-deletable activity");

        var response = await CreateClient().PostAsJsonAsync(
            "api/activity-backlog-profile/batch-delete",
            new { Ids = new[] { firstProfileId, secondProfileId } },
            JsonOpts, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the ownership check must not degrade into a blanket rejection");

        await using var assertDb = CreateDbContext();
        var surviving = await assertDb.Set<ActivityBacklogProfile>().AsNoTracking()
            .CountAsync(p => p.Id == firstProfileId || p.Id == secondProfileId, CancellationToken);
        surviving.Should().Be(0);
    }

    // ---- Cascade: deleting the Activity removes its profiles, no orphan survives -----------------

    [Theory]
    [InlineData("backlog")]
    [InlineData("bucketList")]
    [InlineData("project")]
    public async Task DeletingActivity_CascadesToItsProfile_NoOrphanSurvives(string kind)
    {
        await using var db = CreateDbContext();
        long activityId;
        long profileId;
        switch (kind)
        {
            case "backlog":
                (activityId, profileId) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId);
                break;
            case "bucketList":
                (activityId, profileId) = await SeedBucketListProfileAsync(db, FakeLoggedUserService.TestUserId);
                break;
            default:
                (activityId, profileId) = await SeedProjectProfileAsync(db, FakeLoggedUserService.TestUserId);
                break;
        }

        var deleteResponse = await CreateClient().DeleteAsync($"api/activity/{activityId}", TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        var orphanExists = kind switch
        {
            "backlog" => await assertDb.Set<ActivityBacklogProfile>().AsNoTracking().AnyAsync(p => p.Id == profileId, CancellationToken),
            "bucketList" => await assertDb.Set<ActivityBucketListProfile>().AsNoTracking().AnyAsync(p => p.Id == profileId, CancellationToken),
            _ => await assertDb.Set<ActivityProjectProfile>().AsNoTracking().AnyAsync(p => p.Id == profileId, CancellationToken)
        };
        orphanExists.Should().BeFalse("the profile has no user filter of its own -- an orphan row would be both a correctness and a privacy problem");
    }
}
