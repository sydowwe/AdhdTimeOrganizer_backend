using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;
using static AdhdTimeOrganizer.IntegrationTests.Endpoints.ActivityProfileTestSeedHelper;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// B11 — the five grids accept the field names the client actually sends, one test per grid. B24 adds
/// the two distinct-entries grids for their <c>coverage</c> field.
/// </summary>
/// <remarks>
/// <para>
/// This is the silent failure the ask was written about: <c>IFilterRequest</c> is an empty marker, each
/// grid reads named members off its own record, and System.Text.Json drops unknown members without a
/// word. A client sending <c>roleIds</c> to a record that only declared <c>RoleId</c> got the
/// <em>unfiltered</em> grid with a <b>200</b> — for three months, on five endpoints, with no test on
/// either side able to see it. Which is why every case here seeds a matching row <em>and a decoy</em>
/// and asserts on the ids that come back: a 200 with rows is exactly what the bug looked like.
/// </para>
/// <para>
/// Each case posts the field under the client's camelCase spelling as a raw JSON property name, so the
/// test cannot pass by agreeing with the server's PascalCase member instead of the client's wire name.
/// Adding a member to one of the five filter records means adding a case here.
/// </para>
/// <para>
/// ⚠ <c>useFilter: true</c> is load-bearing: <c>BaseGridEndpoint</c> skips <c>ApplyCustomFiltering</c>
/// entirely when it is false, and every assertion below would pass vacuously.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class GridFilterFieldNameTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    // ---- activity/grid --------------------------------------------------------------------------------

    [Fact(DisplayName = "B11: activity/grid filters by roleIds and categoryIds, any-of")]
    public async Task ActivityGrid_AcceptsClientFieldNames()
    {
        var token = Guid.NewGuid().ToString("N");
        long matchId, decoyId, roleA, roleB, categoryA;
        await using (var db = CreateDbContext())
        {
            roleA = await SeedRoleAsync(db, UserId, $"Role A {token}");
            roleB = await SeedRoleAsync(db, UserId, $"Role B {token}");
            var roleDecoy = await SeedRoleAsync(db, UserId, $"Role decoy {token}");
            categoryA = await SeedCategoryAsync(db, UserId, $"Category A {token}");
            var categoryDecoy = await SeedCategoryAsync(db, UserId, $"Category decoy {token}");

            matchId = await SeedActivityAsync(db, $"Match {token}", roleA, categoryA);
            decoyId = await SeedActivityAsync(db, $"Decoy {token}", roleDecoy, categoryDecoy);
        }

        await AssertNarrowsAsync("api/activity/grid", matchId, decoyId,
            ("roleIds", new Dictionary<string, object?> { ["roleIds"] = new[] { roleA, roleB } }),
            ("categoryIds", new Dictionary<string, object?> { ["categoryIds"] = new[] { categoryA } }));
    }

    /// <summary>
    /// The pin against an over-eager "any-of": the combobox sends <c>[]</c> when the last chip is cleared,
    /// and that has to mean "no opinion", not "match nothing".
    /// </summary>
    [Fact(DisplayName = "B11: an empty id list is no opinion, not an empty grid")]
    public async Task ActivityGrid_EmptyIdList_DoesNotFilter()
    {
        long activityId;
        await using (var db = CreateDbContext())
            activityId = await SeedActivityAsync(db, $"Unfiltered {Guid.NewGuid():N}", await SeedRoleAsync(db, UserId, $"R {Guid.NewGuid():N}"), null);

        var ids = await FetchIdsAsync("api/activity/grid",
            new Dictionary<string, object?> { ["roleIds"] = Array.Empty<long>(), ["categoryIds"] = Array.Empty<long>() });

        ids.Should().Contain(activityId);
    }

    // ---- activity-backlog-profile/grid ----------------------------------------------------------------

    [Fact(DisplayName = "B11: activity-backlog-profile/grid accepts all nine client field names")]
    public async Task BacklogGrid_AcceptsClientFieldNames()
    {
        var token = Guid.NewGuid().ToString("N");
        long matchId, decoyId, locationA, weatherA, costA;
        await using (var db = CreateDbContext())
        {
            locationA = await SeedLookupAsync<ActivityLocationType>(db, UserId, $"Location A {token}");
            weatherA = await SeedLookupAsync<ActivityWeatherDependency>(db, UserId, $"Weather A {token}");
            costA = await SeedLookupAsync<ActivityExpectedCostTier>(db, UserId, $"Cost A {token}");
            var locationDecoy = await SeedLookupAsync<ActivityLocationType>(db, UserId, $"Location decoy {token}");
            var weatherDecoy = await SeedLookupAsync<ActivityWeatherDependency>(db, UserId, $"Weather decoy {token}");
            var costDecoy = await SeedLookupAsync<ActivityExpectedCostTier>(db, UserId, $"Cost decoy {token}");

            matchId = await SeedBacklogAsync(db, $"Hiking match {token}", new ActivityBacklogProfile
            {
                LocationTypeId = locationA, WeatherDependencyId = weatherA, ExpectedCostTierId = costA,
                EnergyLevel = EnergyLevel.High, EffortType = EffortType.Physical,
                DurationMinutes = 30, MinParticipants = 3, MaxParticipants = 4
            });
            decoyId = await SeedBacklogAsync(db, $"Reading decoy {token}", new ActivityBacklogProfile
            {
                LocationTypeId = locationDecoy, WeatherDependencyId = weatherDecoy, ExpectedCostTierId = costDecoy,
                EnergyLevel = EnergyLevel.Low, EffortType = null,
                DurationMinutes = 120, MinParticipants = 1, MaxParticipants = null
            });
        }

        await AssertNarrowsAsync("api/activity-backlog-profile/grid", matchId, decoyId,
            ("activityName", new Dictionary<string, object?> { ["activityName"] = "hiking" }),
            ("locationTypeIds", new Dictionary<string, object?> { ["locationTypeIds"] = new[] { locationA } }),
            ("weatherDependencyIds", new Dictionary<string, object?> { ["weatherDependencyIds"] = new[] { weatherA } }),
            ("expectedCostTierIds", new Dictionary<string, object?> { ["expectedCostTierIds"] = new[] { costA } }),
            ("energyLevels", new Dictionary<string, object?> { ["energyLevels"] = new[] { "High", "Medium" } }),
            ("effortTypes", new Dictionary<string, object?> { ["effortTypes"] = new[] { "Physical" } }),
            ("maxDurationMinutes", new Dictionary<string, object?> { ["maxDurationMinutes"] = 30 }),
            ("minParticipants", new Dictionary<string, object?> { ["minParticipants"] = 3 }),
            ("maxParticipants", new Dictionary<string, object?> { ["maxParticipants"] = 4 }));
    }

    // ---- activity-bucket-list-profile/grid ------------------------------------------------------------

    [Fact(DisplayName = "B11: activity-bucket-list-profile/grid accepts all four client field names")]
    public async Task BucketListGrid_AcceptsClientFieldNames()
    {
        var token = Guid.NewGuid().ToString("N");
        long matchId, decoyId, experienceA;
        await using (var db = CreateDbContext())
        {
            experienceA = await SeedLookupAsync<ActivityExperienceType>(db, UserId, $"Experience A {token}");
            var experienceDecoy = await SeedLookupAsync<ActivityExperienceType>(db, UserId, $"Experience decoy {token}");

            matchId = await SeedBucketListAsync(db, $"Skydiving match {token}", experienceA, comfortZoneStep: 5);
            decoyId = await SeedBucketListAsync(db, $"Museum decoy {token}", experienceDecoy, comfortZoneStep: 1);
        }

        await AssertNarrowsAsync("api/activity-bucket-list-profile/grid", matchId, decoyId,
            ("activityName", new Dictionary<string, object?> { ["activityName"] = "SKYDIVING" }),
            ("experienceTypeIds", new Dictionary<string, object?> { ["experienceTypeIds"] = new[] { experienceA } }),
            ("minComfortZoneStep", new Dictionary<string, object?> { ["minComfortZoneStep"] = 5 }),
            // Inclusive at the top too: the match sits exactly on the bound, the decoy (1) is under a
            // max of 5 as well, so the bound is paired with a min to keep the case honest.
            ("maxComfortZoneStep", new Dictionary<string, object?> { ["minComfortZoneStep"] = 2, ["maxComfortZoneStep"] = 5 }));
    }

    // ---- memory-anchor/grid ---------------------------------------------------------------------------

    [Fact(DisplayName = "B11: memory-anchor/grid accepts activityName and minRating")]
    public async Task MemoryAnchorGrid_AcceptsClientFieldNames()
    {
        var token = Guid.NewGuid().ToString("N");
        long matchId, decoyId;
        await using (var db = CreateDbContext())
        {
            var matchActivity = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Concert match {token}");
            var decoyActivity = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Dentist decoy {token}");
            matchId = await SeedAnchorAsync(db, matchActivity, rating: 9);
            decoyId = await SeedAnchorAsync(db, decoyActivity, rating: 3);
        }

        await AssertNarrowsAsync("api/memory-anchor/grid", matchId, decoyId,
            ("activityName", new Dictionary<string, object?> { ["activityName"] = "concert" }),
            ("minRating", new Dictionary<string, object?> { ["minRating"] = 9 }));
    }

    // ---- activity-project-profile/grid ----------------------------------------------------------------

    [Fact(DisplayName = "B11: activity-project-profile/grid accepts activityName, difficultyLevels and readinessStatuses")]
    public async Task ProjectGrid_AcceptsClientFieldNames()
    {
        var token = Guid.NewGuid().ToString("N");
        long matchId, decoyId;
        await using (var db = CreateDbContext())
        {
            matchId = await SeedProjectAsync(db, $"Bookshelf match {token}", DifficultyLevel.Expert, ReadinessStatus.ReadyToStart);
            decoyId = await SeedProjectAsync(db, $"Birdhouse decoy {token}", DifficultyLevel.Beginner, ReadinessStatus.Planning);
        }

        await AssertNarrowsAsync("api/activity-project-profile/grid", matchId, decoyId,
            ("activityName", new Dictionary<string, object?> { ["activityName"] = "bookshelf" }),
            ("difficultyLevels", new Dictionary<string, object?> { ["difficultyLevels"] = new[] { "Expert", "Intermediate" } }),
            ("readinessStatuses", new Dictionary<string, object?> { ["readinessStatuses"] = new[] { "ReadyToStart" } }));
    }

    // ---- activity-tracking/desktop/grid + android/grid (B24) ------------------------------------------

    /// <summary>
    /// B24's <c>coverage</c> field on the desktop distinct-entries grid. The match is a tuple an active rule
    /// covers, the decoy one nothing covers; <c>Covered</c> has to keep the first and drop the second. The
    /// token-narrowing <c>processName</c> rides along so the page is this test's own rows (<c>PERF-4</c>:
    /// the grid has no date bound).
    /// </summary>
    [Fact(DisplayName = "B24: activity-tracking/desktop/grid filters by coverage")]
    public async Task DesktopDistinctGrid_AcceptsClientFieldNames()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        long matchId, decoyId;
        await using (var db = CreateDbContext())
        {
            var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, $"Covered {token}");
            db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
            {
                UserId = UserId, ProcessName = $"match-{token}.exe", ProcessNameMatchType = PatternMatchType.Exact,
                IsActive = true, ActivityId = activityId
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            matchId = await SeedDesktopEntryAsync(db, $"match-{token}.exe", minuteOffset: 0);
            decoyId = await SeedDesktopEntryAsync(db, $"decoy-{token}.exe", minuteOffset: 1);
        }

        await AssertNarrowsAsync("api/activity-tracking/desktop/grid", matchId, decoyId,
            ("coverage", new Dictionary<string, object?>
            {
                ["processName"] = $"-{token}.exe", ["processNameMatchType"] = "Contains", ["coverage"] = "Covered"
            }));
    }

    /// <summary>The android twin — same field, same wire spelling, its own rule table.</summary>
    [Fact(DisplayName = "B24: activity-tracking/android/grid filters by coverage")]
    public async Task AndroidDistinctGrid_AcceptsClientFieldNames()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        long matchId, decoyId;
        await using (var db = CreateDbContext())
        {
            db.Set<TrackerAndroidMappingByPattern>().Add(new TrackerAndroidMappingByPattern
            {
                UserId = UserId, PackageName = $"com.match.{token}", PackageNameMatchType = PatternMatchType.Exact,
                IsActive = true, IsIgnored = true
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            matchId = await SeedAndroidSessionAsync(db, $"com.match.{token}", minuteOffset: 0);
            decoyId = await SeedAndroidSessionAsync(db, $"com.decoy.{token}", minuteOffset: 1);
        }

        await AssertNarrowsAsync("api/activity-tracking/android/grid", matchId, decoyId,
            ("coverage", new Dictionary<string, object?>
            {
                ["packageName"] = $".{token}", ["packageNameMatchType"] = "Contains", ["coverage"] = "Covered"
            }));
    }

    // ---- helpers --------------------------------------------------------------------------------------

    /// <summary>
    /// Posts each filter on its own and checks it keeps the match and drops the decoy. The field name is in
    /// the failure message because "one of nine assertions failed" is not a diagnosis.
    /// </summary>
    private async Task AssertNarrowsAsync(string route, long matchId, long decoyId, params (string field, object filter)[] cases)
    {
        foreach (var (field, filter) in cases)
        {
            var ids = await FetchIdsAsync(route, filter);
            ids.Should().Contain(matchId, "'{0}' must keep the row it describes", field);
            ids.Should().NotContain(decoyId,
                "'{0}' must drop the decoy — a 200 that returns everything is what an unread member looks like", field);
        }
    }

    private async Task<List<long>> FetchIdsAsync(string route, object filter)
    {
        var response = await CreateClient().PostAsJsonAsync(route, new
        {
            page = 1,
            itemsPerPage = 100,
            sortBy = Array.Empty<object>(),
            useFilter = true,
            filter
        }, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonDocument.Parse(body).RootElement.GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();
    }

    private static async Task<long> SeedActivityAsync(DbContext db, string name, long roleId, long? categoryId)
    {
        var activity = new Activity { UserId = UserId, Name = name, RoleId = roleId, CategoryId = categoryId };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return activity.Id;
    }

    private static async Task<long> SeedBacklogAsync(DbContext db, string activityName, ActivityBacklogProfile profile)
    {
        profile.ActivityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, activityName);
        db.Set<ActivityBacklogProfile>().Add(profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return profile.Id;
    }

    private static async Task<long> SeedBucketListAsync(DbContext db, string activityName, long experienceTypeId, int comfortZoneStep)
    {
        var profile = new ActivityBucketListProfile
        {
            ActivityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, activityName),
            ExperienceTypeId = experienceTypeId,
            ComfortZoneStep = comfortZoneStep,
            InspirationSource = "B11"
        };
        db.Set<ActivityBucketListProfile>().Add(profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return profile.Id;
    }

    private static async Task<long> SeedProjectAsync(DbContext db, string activityName, DifficultyLevel difficulty, ReadinessStatus readiness)
    {
        var profile = new ActivityProjectProfile
        {
            ActivityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, activityName),
            DifficultyLevel = difficulty,
            ReadinessStatus = readiness,
            ProjectArea = "Woodworking",
            EstimatedHours = 4
        };
        db.Set<ActivityProjectProfile>().Add(profile);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return profile.Id;
    }

    /// <summary>Inside the 2026 partition and UTC — see <c>TrackingDistinctEntryGridTests</c> for why both matter.</summary>
    private static readonly DateTime TrackingInstant = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<long> SeedDesktopEntryAsync(DbContext db, string processName, int minuteOffset)
    {
        var entry = new DesktopActivityEntry
        {
            UserId = UserId, WindowStart = TrackingInstant.AddMinutes(minuteOffset),
            ProcessName = processName, ProductName = "B24", WindowTitle = "a window", ExecutablePath = $"/usr/bin/{processName}",
            IsFullscreen = false, ActiveSeconds = 60, BackgroundSeconds = 0, IsPlayingSound = false, ActiveMonitor = 0
        };
        db.Set<DesktopActivityEntry>().Add(entry);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return entry.Id;
    }

    private static async Task<long> SeedAndroidSessionAsync(DbContext db, string packageName, int minuteOffset)
    {
        var start = TrackingInstant.AddMinutes(minuteOffset);
        var session = new AndroidSessionData
        {
            UserId = UserId, DeviceId = "b24-device", PackageName = packageName, AppLabel = "B24",
            SessionStartUtc = start, SessionEndUtc = start.AddSeconds(300), DurationSeconds = 300
        };
        db.Set<AndroidSessionData>().Add(session);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return session.Id;
    }

    private static async Task<long> SeedAnchorAsync(DbContext db, long activityId, int rating)
    {
        var anchor = new MemoryAnchor
        {
            UserId = UserId, ActivityId = activityId, AnchorYear = 2025, AnchorMonth = 6,
            HighlightNote = "B11", Rating = rating
        };
        db.Set<MemoryAnchor>().Add(anchor);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return anchor.Id;
    }
}
