using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.@base;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
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
    private const string Password = "Test@1234!";

    // ---- seeding helpers -------------------------------------------------

    private async Task<long> CreateSecondUserAsync(string email)
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

    private static async Task<long> SeedRoleAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = name, Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);
        return role.Id;
    }

    private static async Task<long> SeedActivityAsync(DbContext db, long userId, string name)
    {
        var roleId = await SeedRoleAsync(db, userId, $"{name} Role");
        var activity = new Activity { UserId = userId, Name = name, RoleId = roleId };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private static async Task<long> SeedLookupAsync<TLookup>(DbContext db, long userId, string text)
        where TLookup : BaseLookupWithUser, new()
    {
        var lookup = new TLookup { UserId = userId, Text = text };
        db.Set<TLookup>().Add(lookup);
        await db.SaveChangesAsync(CancellationToken);
        return lookup.Id;
    }

    private async Task<(long activityId, long profileId)> SeedBacklogProfileAsync(DbContext db, long userId, string name = "Backlog Activity",
        bool isRepeatable = false)
    {
        var activityId = await SeedActivityAsync(db, userId, name);
        var locationTypeId = await SeedLookupAsync<ActivityLocationType>(db, userId, $"{name} Home");
        var weatherId = await SeedLookupAsync<ActivityWeatherDependency>(db, userId, $"{name} Indoor");
        var costTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, userId, $"{name} Free");

        var profile = new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherId,
            ExpectedCostTierId = costTierId,
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30,
            IsRepeatable = isRepeatable
        };
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return (activityId, profile.Id);
    }

    private async Task<(long activityId, long profileId)> SeedBucketListProfileAsync(DbContext db, long userId, string name = "Bucket Activity")
    {
        var activityId = await SeedActivityAsync(db, userId, name);
        var experienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, userId, $"{name} Adventure");

        var profile = new ActivityBucketListProfile
        {
            ActivityId = activityId,
            ExperienceTypeId = experienceTypeId,
            ComfortZoneStep = 5,
            InspirationSource = "A friend's story"
        };
        db.Set<ActivityBucketListProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return (activityId, profile.Id);
    }

    private async Task<(long activityId, long profileId)> SeedProjectProfileAsync(DbContext db, long userId, string name = "Project Activity")
    {
        var activityId = await SeedActivityAsync(db, userId, name);

        var profile = new ActivityProjectProfile
        {
            ActivityId = activityId,
            DifficultyLevel = DifficultyLevel.Beginner,
            ProjectArea = "Woodworking",
            EstimatedHours = 4,
            ReadinessStatus = ReadinessStatus.Planning
        };
        db.Set<ActivityProjectProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return (activityId, profile.Id);
    }

    private static async Task<long> SeedMemoryAnchorAsync(DbContext db, long userId, long activityId, int year, int month)
    {
        var anchor = new MemoryAnchor
        {
            UserId = userId,
            ActivityId = activityId,
            AnchorYear = year,
            AnchorMonth = month,
            HighlightNote = $"Anchored {year}-{month:00}",
            Rating = 8
        };
        db.Set<MemoryAnchor>().Add(anchor);
        await db.SaveChangesAsync(CancellationToken);
        return anchor.Id;
    }

    private static object GridBody() => new { useFilter = false, filter = new { }, sortBy = Array.Empty<object>(), itemsPerPage = 20, page = 1 };

    private static object FilteredGridBody(object filter, object[]? sortBy = null, int itemsPerPage = 20) =>
        new { useFilter = true, filter, sortBy = sortBy ?? [], itemsPerPage, page = 1 };

    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    private static long[] IdsOf(GridResponse body) => body.Items.Select(i => i.GetProperty("id").GetInt64()).ToArray();

    private static JsonElement ItemWithId(GridResponse body, long id) =>
        body.Items.Single(i => i.GetProperty("id").GetInt64() == id);

    private static long? MemoryAnchorIdOf(JsonElement item) =>
        item.GetProperty("memoryAnchorId").ValueKind == JsonValueKind.Null ? null : item.GetProperty("memoryAnchorId").GetInt64();

    // ---- A: backlog ---------------------------------------------------------

    [Fact]
    public async Task Backlog_Grid_OnlyReturnsCallersOwnRows()
    {
        var userIdB = await CreateSecondUserAsync("backlog-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId, "A's backlog activity");
        var (_, profileIdB) = await SeedBacklogProfileAsync(db, userIdB, "B's backlog activity");

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().PostAsJsonAsync("api/activity-backlog-profile/grid", GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts);
        body!.Items.Select(i => i.GetProperty("id").GetInt64()).Should().Contain(profileIdB);
        body.Items.Select(i => i.GetProperty("id").GetInt64()).Should().NotContain(profileIdA);
    }

    [Fact]
    public async Task Backlog_GetById_ForeignProfile_Returns404()
    {
        var userIdB = await CreateSecondUserAsync("backlog-getbyid-b@test.com");
        await using var db = CreateDbContext();
        var (_, profileIdA) = await SeedBacklogProfileAsync(db, FakeLoggedUserService.TestUserId);

        await using var factoryB = CreateFactory(["User"], userIdB);
        var response = await factoryB.CreateClient().GetAsync($"api/activity-backlog-profile/{profileIdA}");

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
        var updateResponse = await clientB.PutAsJsonAsync($"api/activity-backlog-profile/{profileIdA}", updatePayload, JsonOpts);
        updateResponse.IsSuccessStatusCode.Should().BeFalse();

        var deleteResponse = await clientB.DeleteAsync($"api/activity-backlog-profile/{profileIdA}");
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
        var response = await factoryB.CreateClient().GetAsync("api/activity-backlog-profile/all-options");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
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

        var response = await CreateClient().PostAsJsonAsync("api/activity-backlog-profile", payload, JsonOpts);

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
        var response = await factoryB.CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile/grid", GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts);
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
        var response = await factoryB.CreateClient().GetAsync($"api/activity-bucket-list-profile/{profileIdA}");

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
            ComfortZoneStep = 9,
            RequiresTravel = true,
            InspirationSource = "Hijacked"
        };
        var updateResponse = await clientB.PutAsJsonAsync($"api/activity-bucket-list-profile/{profileIdA}", updatePayload, JsonOpts);
        updateResponse.IsSuccessStatusCode.Should().BeFalse();

        var deleteResponse = await clientB.DeleteAsync($"api/activity-bucket-list-profile/{profileIdA}");
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

        var response = await CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
        var response = await factoryB.CreateClient().PostAsJsonAsync("api/activity-project-profile/grid", GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts);
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
        var response = await factoryB.CreateClient().GetAsync($"api/activity-project-profile/{profileIdA}");

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
        var updateResponse = await clientB.PutAsJsonAsync($"api/activity-project-profile/{profileIdA}", updatePayload, JsonOpts);
        updateResponse.IsSuccessStatusCode.Should().BeFalse();

        var deleteResponse = await clientB.DeleteAsync($"api/activity-project-profile/{profileIdA}");
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
            JsonOpts);

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
            JsonOpts);

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

        var response = await CreateClient().PostAsJsonAsync("api/activity-project-profile", payload, JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

        var response = await CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile/grid", GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!;

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
            JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!;
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
            JsonOpts);

        // isAnchored is computed in the projection, not stored -- if it were overlaid after the query the
        // sort would silently run on `false` for every row and this would pass by accident half the time.
        ascending.StatusCode.Should().Be(HttpStatusCode.OK);
        IdsOf((await ascending.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!)
            .Should().Equal([todoProfileId, doneProfileId], "ascending puts the entries still to do first");

        var descending = await client.PostAsJsonAsync(
            "api/activity-bucket-list-profile/grid",
            FilteredGridBody(new { }, [new { key = "isAnchored", isDesc = true }]),
            JsonOpts);

        descending.StatusCode.Should().Be(HttpStatusCode.OK);
        IdsOf((await descending.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!)
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

        var response = await CreateClient().PostAsJsonAsync("api/activity-backlog-profile/grid", GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!;

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
            JsonOpts);
        denominator.StatusCode.Should().Be(HttpStatusCode.OK);
        (await denominator.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!.ItemsCount.Should().Be(2);

        var numerator = await client.PostAsJsonAsync(
            "api/activity-backlog-profile/grid",
            FilteredGridBody(new { isOneTime = true, isAnchored = true }, itemsPerPage: 1),
            JsonOpts);
        numerator.StatusCode.Should().Be(HttpStatusCode.OK);
        var numeratorBody = (await numerator.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!;
        numeratorBody.ItemsCount.Should().Be(1);
        IdsOf(numeratorBody).Should().Equal(doneProfileId);

        // isAnchored:false must agree with the response field, so the repeatable entry lands here too.
        var notYet = await client.PostAsJsonAsync(
            "api/activity-backlog-profile/grid",
            FilteredGridBody(new { isAnchored = false }),
            JsonOpts);
        notYet.StatusCode.Should().Be(HttpStatusCode.OK);
        var notYetBody = (await notYet.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!;
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

        var response = await CreateClient().PostAsJsonAsync("api/activity-bucket-list-profile/grid", GridBody(), JsonOpts);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts))!;
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

        var response = await CreateClient().GetAsync($"api/activity-bucket-list-profile/{profileId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        item.GetProperty("isAnchored").GetBoolean()
            .Should().BeTrue("GetById projects in memory and cannot reach the anchors -- it must overlay them, not answer false");
        item.GetProperty("memoryAnchorId").GetInt64().Should().Be(anchorId);
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

        var deleteResponse = await CreateClient().DeleteAsync($"api/activity/{activityId}");
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
