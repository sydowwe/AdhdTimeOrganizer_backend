using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The three target ids a pattern mapping carries — `ActivityId`, `RoleId`, `CategoryId` — on the four
/// endpoints that write them raw: desktop and android mapping create <b>and update</b>.
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
/// `BeforeMapping` on all four endpoints, over `IReferencedIdOwnershipService`. 404 rather than
/// 403, for the same existence-oracle reason as the activity guard. The update half arrived with the
/// target members themselves (CQ-10) — the guard had nothing to check there before.
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

    // ---- the update surface (CQ-10) ---------------------------------------------------------------

    /// <summary>
    /// The half that did not exist when SEC-2 was closed. `UpdateEntity` wrote the pattern fields and
    /// nothing else, so a mapping could not be re-pointed at all; CQ-10 put the three target ids on the
    /// update DTOs, and a re-pointing update is the more dangerous of the two writes — it can aim a rule
    /// that is *already matching the user's traffic* at a stranger's activity, with no create to notice.
    /// </summary>
    [Fact(DisplayName = "Re-pointing a desktop mapping onto another user's activity is rejected")]
    public async Task UpdateDesktopMapping_WithForeignActivityId_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignActivityId, mappingId;
        await using (var db = CreateDbContext())
        {
            foreignActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "Foreign update target", foreignUserId, CancellationToken);
            mappingId = await SeedOwnDesktopMappingAsync(db);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"{DesktopRoute}/{mappingId}",
            new { processName = "repointed.exe", processNameMatchType = "Exact", isActive = true, activityId = foreignActivityId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var assertDb = CreateDbContext();
        var after = await assertDb.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == mappingId, CancellationToken);
        after.ActivityId.Should().BeNull("the guard runs before the mapping, so nothing is written at all");
        after.IsIgnored.Should().BeTrue("the row must keep the target it had, not lose it to a rejected update");
    }

    /// <summary>
    /// The control, and CQ-10's own point: re-pointing to one's own activity is exactly what the update
    /// route exists for. Before this the user had to delete the rule and write it again — and losing a
    /// mapping means every future heartbeat matching that pattern silently stops being attributed.
    /// </summary>
    [Fact(DisplayName = "Re-pointing a desktop mapping onto the caller's own activity succeeds")]
    public async Task UpdateDesktopMapping_CanRepointOntoTheCallersOwnActivity()
    {
        long ownActivityId, mappingId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own update target", ct: CancellationToken);
            mappingId = await SeedOwnDesktopMappingAsync(db);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"{DesktopRoute}/{mappingId}",
            new { processName = "repointed.exe", processNameMatchType = "Exact", isActive = true, activityId = ownActivityId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var after = await assertDb.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == mappingId, CancellationToken);
        after.ActivityId.Should().Be(ownActivityId);
        after.IsIgnored.Should().BeNull("the target group is exclusive — taking the activity clears IsIgnored");
    }

    [Fact(DisplayName = "Re-pointing an android mapping onto another user's activity is rejected")]
    public async Task UpdateAndroidMapping_WithForeignActivityId_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignActivityId, mappingId;
        await using (var db = CreateDbContext())
        {
            foreignActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "Foreign android update target", foreignUserId, CancellationToken);
            mappingId = await SeedOwnAndroidMappingAsync(db);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"{AndroidRoute}/{mappingId}",
            new { packageName = "com.repointed.app", packageNameMatchType = "Exact", isActive = true, activityId = foreignActivityId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountMappingsAsync<TrackerAndroidMappingByPattern>("ActivityId", foreignActivityId)).Should().Be(0);
    }

    /// <summary>
    /// The trap CQ-10's text names: the update validator omitted the create validator's target rules
    /// because update had no targets to check. An update is a full replace, so a body naming no target
    /// clears all four columns — and <c>CK_TrackerDesktopMappingByPattern_TargetRequired</c> would answer
    /// that with a 23514 the user sees as a 500. The rules moved onto both validators with the members.
    /// </summary>
    [Fact(DisplayName = "An update naming no target at all is a 400, not a check-constraint 500")]
    public async Task UpdateDesktopMapping_WithNoTarget_IsRejectedByTheValidator()
    {
        long mappingId;
        await using (var db = CreateDbContext())
            mappingId = await SeedOwnDesktopMappingAsync(db);

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"{DesktopRoute}/{mappingId}",
            new { processName = "targetless.exe", processNameMatchType = "Exact", isActive = true },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("Exactly one target", "the 400 has to say which rule the body broke");
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private async Task<long> SeedOwnDesktopMappingAsync(DbContext db)
    {
        var mapping = new TrackerDesktopMappingByPattern
        {
            UserId = FakeLoggedUserService.TestUserId,
            ProcessName = $"own-{Guid.NewGuid():N}.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            IsIgnored = true
        };
        db.Set<TrackerDesktopMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(CancellationToken);
        return mapping.Id;
    }

    private async Task<long> SeedOwnAndroidMappingAsync(DbContext db)
    {
        var mapping = new TrackerAndroidMappingByPattern
        {
            UserId = FakeLoggedUserService.TestUserId,
            PackageName = $"com.own.{Guid.NewGuid():N}",
            PackageNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            IsIgnored = true
        };
        db.Set<TrackerAndroidMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(CancellationToken);
        return mapping.Id;
    }

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
