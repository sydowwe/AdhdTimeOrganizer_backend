using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The three target ids a pattern mapping carries — `ActivityId`, `RoleId`, `CategoryId` — on the two
/// endpoints that write them raw: desktop and android mapping create.
/// </summary>
/// <remarks>
/// <para>
/// Same hole as <see cref="ActivityIdOwnershipTests"/>, but a mapping is a <b>standing rule</b> rather
/// than a single row: every later heartbeat matching the pattern would attribute the caller's seconds
/// to the stranger's activity and publish `ActivityTimeRecordedEvent` with it. The FK is satisfied
/// (the row exists), the caller's own `UserId` is stamped on the mapping, and read-back loses the
/// activity to the query filter — so the write leaves no visible symptom to report.
/// </para>
/// <para>
/// The guard is `MappingTargetOwnershipExtensions.EnsureOwnsMappingTargetsAsync`, called from
/// `BeforeMapping` on both create endpoints, over `IReferencedIdOwnershipService`. 404 rather than
/// 403, for the same existence-oracle reason as the activity guard.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TrackerMappingTargetOwnershipTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string DesktopRoute = "/api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern";
    private const string AndroidRoute = "/api/activity-tracking/android/settings/tracker-android-mapping-by-pattern";

    [Fact(DisplayName = "Creating a desktop mapping onto another user's activity is rejected")]
    public async Task CreateDesktopMapping_WithForeignActivityId_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignActivityId;
        await using (var db = CreateDbContext())
            foreignActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "Foreign desktop activity", foreignUserId, CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            DesktopRoute,
            new { processName = "stranger.exe", processNameMatchType = "Exact", isActive = true, activityId = foreignActivityId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an activity the caller does not own must read as one that does not exist");
        (await CountMappingsAsync<TrackerDesktopMappingByPattern>("ActivityId", foreignActivityId)).Should().Be(0);
    }

    /// <summary>The control for the rejection above — without it the endpoint could be hard-wired to 404.</summary>
    [Fact(DisplayName = "Creating a desktop mapping onto the caller's own activity still succeeds")]
    public async Task CreateDesktopMapping_WithOwnActivityId_Succeeds()
    {
        long ownActivityId;
        await using (var db = CreateDbContext())
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own desktop activity", ct: CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            DesktopRoute,
            new { processName = "own.exe", processNameMatchType = "Exact", isActive = true, activityId = ownActivityId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountMappingsAsync<TrackerDesktopMappingByPattern>("ActivityId", ownActivityId)).Should().Be(1);
    }

    [Fact(DisplayName = "Creating a desktop mapping onto another user's role and category is rejected")]
    public async Task CreateDesktopMapping_WithForeignRoleAndCategoryId_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignRoleId, foreignCategoryId;
        await using (var db = CreateDbContext())
        {
            var role = new ActivityRole { UserId = foreignUserId, Name = $"Foreign role {Guid.NewGuid():N}", Color = "#123456" };
            var category = new ActivityCategory { UserId = foreignUserId, Name = $"Foreign category {Guid.NewGuid():N}", Color = "#654321" };
            db.Set<ActivityRole>().Add(role);
            db.Set<ActivityCategory>().Add(category);
            await db.SaveChangesAsync(CancellationToken);
            foreignRoleId = role.Id;
            foreignCategoryId = category.Id;
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            DesktopRoute,
            new
            {
                windowTitle = "*stranger*",
                windowTitleMatchType = "Wildcard",
                isActive = true,
                roleId = foreignRoleId,
                categoryId = foreignCategoryId
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the role/category target group is as much a standing attribution rule as the activity one");
        (await CountMappingsAsync<TrackerDesktopMappingByPattern>("RoleId", foreignRoleId)).Should().Be(0);
        (await CountMappingsAsync<TrackerDesktopMappingByPattern>("CategoryId", foreignCategoryId)).Should().Be(0);
    }

    [Fact(DisplayName = "Creating an android mapping onto another user's activity is rejected")]
    public async Task CreateAndroidMapping_WithForeignActivityId_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignActivityId;
        await using (var db = CreateDbContext())
            foreignActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "Foreign android activity", foreignUserId, CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            AndroidRoute,
            new { packageName = "com.stranger.app", packageNameMatchType = "Exact", isActive = true, activityId = foreignActivityId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountMappingsAsync<TrackerAndroidMappingByPattern>("ActivityId", foreignActivityId)).Should().Be(0);
    }

    [Fact(DisplayName = "Creating an android mapping onto the caller's own activity still succeeds")]
    public async Task CreateAndroidMapping_WithOwnActivityId_Succeeds()
    {
        long ownActivityId;
        await using (var db = CreateDbContext())
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own android activity", ct: CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            AndroidRoute,
            new { packageName = "com.own.app", packageNameMatchType = "Exact", isActive = true, activityId = ownActivityId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountMappingsAsync<TrackerAndroidMappingByPattern>("ActivityId", ownActivityId)).Should().Be(1);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private Task<long> SeedForeignUserAsync() =>
        ActivityMergeTestSupport.SecondUserAsync(Fixture, $"tracker-mapping-ownership-{Guid.NewGuid():N}@test.com");

    private async Task<int> CountMappingsAsync<TEntity>(string idProperty, long id) where TEntity : class
    {
        await using var db = CreateDbContext();
        return await db.Set<TEntity>()
            .IgnoreQueryFilters()
            .CountAsync(e => EF.Property<long?>(e, idProperty) == id, CancellationToken);
    }
}
