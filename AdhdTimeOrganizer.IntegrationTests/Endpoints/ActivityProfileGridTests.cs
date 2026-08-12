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

    private async Task<(long activityId, long profileId)> SeedBacklogProfileAsync(DbContext db, long userId, string name = "Backlog Activity")
    {
        var activityId = await SeedActivityAsync(db, userId, name);
        var locationTypeId = await SeedLookupAsync<ActivityLocationType>(db, userId, "Home");
        var weatherId = await SeedLookupAsync<ActivityWeatherDependency>(db, userId, "Indoor");
        var costTierId = await SeedLookupAsync<ActivityExpectedCostTier>(db, userId, "Free");

        var profile = new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = locationTypeId,
            WeatherDependencyId = weatherId,
            ExpectedCostTierId = costTierId,
            EnergyLevel = EnergyLevel.Low,
            MinParticipants = 1,
            DurationMinutes = 30
        };
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(CancellationToken);
        return (activityId, profile.Id);
    }

    private async Task<(long activityId, long profileId)> SeedBucketListProfileAsync(DbContext db, long userId, string name = "Bucket Activity")
    {
        var activityId = await SeedActivityAsync(db, userId, name);
        var experienceTypeId = await SeedLookupAsync<ActivityExperienceType>(db, userId, "Adventure");

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

    private static object GridBody() => new { useFilter = false, filter = new { }, sortBy = Array.Empty<object>(), itemsPerPage = 20, page = 1 };

    private record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

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
