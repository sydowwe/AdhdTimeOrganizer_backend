using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
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

    /// <summary>
    /// The role and category ids again, but one at a time. The combined case above passes as soon as
    /// <b>either</b> field is checked, and the guard is three independent <c>if</c>s over three different
    /// <c>IReferencedIdOwnershipService</c> methods resolved through three seam keys — dropping one of
    /// them is a silent partial regression the combined case cannot see.
    /// </summary>
    [Fact(DisplayName = "Creating a desktop mapping onto another user's role alone is rejected")]
    public async Task CreateDesktopMapping_WithForeignRoleIdOnly_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignRoleId;
        await using (var db = CreateDbContext())
        {
            var role = new ActivityRole { UserId = foreignUserId, Name = $"Foreign role {Guid.NewGuid():N}", Color = "#123456" };
            db.Set<ActivityRole>().Add(role);
            await db.SaveChangesAsync(CancellationToken);
            foreignRoleId = role.Id;
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            DesktopRoute,
            new { processName = $"role-only-{Guid.NewGuid():N}.exe", processNameMatchType = "Exact", isActive = true, roleId = foreignRoleId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountMappingsAsync<TrackerDesktopMappingByPattern>("RoleId", foreignRoleId)).Should().Be(0);
    }

    [Fact(DisplayName = "Creating a desktop mapping onto another user's category alone is rejected")]
    public async Task CreateDesktopMapping_WithForeignCategoryIdOnly_IsRejected()
    {
        var foreignUserId = await SeedForeignUserAsync();
        long foreignCategoryId;
        await using (var db = CreateDbContext())
        {
            var category = new ActivityCategory { UserId = foreignUserId, Name = $"Foreign category {Guid.NewGuid():N}", Color = "#654321" };
            db.Set<ActivityCategory>().Add(category);
            await db.SaveChangesAsync(CancellationToken);
            foreignCategoryId = category.Id;
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            DesktopRoute,
            new { processName = $"category-only-{Guid.NewGuid():N}.exe", processNameMatchType = "Exact", isActive = true, categoryId = foreignCategoryId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountMappingsAsync<TrackerDesktopMappingByPattern>("CategoryId", foreignCategoryId)).Should().Be(0);
    }

    /// <summary>
    /// The control for the two above: the role/category target group is a legitimate way to write a rule,
    /// and rejecting all of them would satisfy every negative case here.
    /// </summary>
    [Fact(DisplayName = "Creating a desktop mapping onto the caller's own role and category still succeeds")]
    public async Task CreateDesktopMapping_WithOwnRoleAndCategoryId_Succeeds()
    {
        long ownRoleId, ownCategoryId;
        await using (var db = CreateDbContext())
        {
            var role = new ActivityRole
            {
                UserId = FakeLoggedUserService.TestUserId, Name = $"Own role {Guid.NewGuid():N}", Color = "#123456"
            };
            var category = new ActivityCategory
            {
                UserId = FakeLoggedUserService.TestUserId, Name = $"Own category {Guid.NewGuid():N}", Color = "#654321"
            };
            db.Set<ActivityRole>().Add(role);
            db.Set<ActivityCategory>().Add(category);
            await db.SaveChangesAsync(CancellationToken);
            ownRoleId = role.Id;
            ownCategoryId = category.Id;
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            DesktopRoute,
            new
            {
                processName = $"own-role-{Guid.NewGuid():N}.exe",
                processNameMatchType = "Exact",
                isActive = true,
                roleId = ownRoleId,
                categoryId = ownCategoryId
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountMappingsAsync<TrackerDesktopMappingByPattern>("RoleId", ownRoleId)).Should().Be(1);
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

/// <summary>
/// The consequence half of SEC-2: what a mapping that <em>already</em> points at another user's activity
/// does on the ingest path. The mapping is seeded straight into the database rather than posted, so this
/// is independent of whether the create/update guard above holds.
/// </summary>
/// <remarks>
/// <para>
/// The guard on the four settings endpoints is the <b>only</b> thing standing between a foreign target id
/// and the ledger. <c>DesktopActivityHeartbeatEndpoint.HandleAsync</c> loads the caller's active mappings,
/// takes <c>match.ActivityId</c> at face value and hands it to
/// <c>IActivityTimeAttributionSink.AttributeAsync</c>, which filters its read on the <em>caller's</em>
/// <c>UserId</c> and then writes an <c>ActivityHistory</c> row keyed on that user and that activity — the
/// pair is never checked against each other, and no FK spans both columns
/// (<see cref="TrackerPatternMappingActivityFkTests"/>).
/// </para>
/// <para>
/// So a row written before SEC-2 was closed, or one that outlives some future path that writes the target
/// without the guard, keeps attributing indefinitely and silently: the write is a 200, the read-back loses
/// the activity to the query filter, and the victim never sees anything at all.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TrackerMappingForeignTargetHeartbeatTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string HeartbeatRoute = "/api/activity-tracking/desktop/heartbeat";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    /// <summary>Pinned rather than derived from <c>UtcNow</c>, as in <see cref="DesktopHeartbeatIngestTests"/>:
    /// a run crossing UTC midnight would seed one day and assert on another.</summary>
    private static readonly DateTime NineAm = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    private ITestClientFactory? _trackingFactory;

    /// <summary>
    /// <b>SEC-2 residual, asserted as it behaves today.</b> The heartbeat attributes the caller's seconds to
    /// the stranger's activity, exactly as the finding describes.
    /// <para>
    /// <b>Intended behaviour:</b> a mapping whose target the caller does not own must not attribute — the
    /// entry should land as a raw <c>DesktopActivityEntry</c> and nothing else, the same outcome as
    /// <c>DesktopHeartbeat_DoesNotAttributeAnEntryWithNoMatchingMapping</c>. When that check is added
    /// (an ownership join on the mapping read, or a guard around the <c>AttributeAsync</c> call), flip the
    /// two assertions below to expect no <c>ActivityHistory</c> row and drop the trait.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "A heartbeat matching a mapping onto another user's activity attributes to it anyway (SEC-2 residual)")]
    [Trait("Status", "KnownGap")]
    public async Task Heartbeat_WithMappingOntoAnotherUsersActivity_StillAttributesToIt()
    {
        var foreignUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"tracker-mapping-heartbeat-{Guid.NewGuid():N}@test.com");

        long foreignActivityId;
        await using (var db = CreateDbContext())
        {
            foreignActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, "Foreign heartbeat target", foreignUserId, CancellationToken);

            // Seeded directly: the create endpoint rejects this payload now, and the point of the case is
            // what the ingest path does with such a row however it got there.
            db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
            {
                UserId = UserId,
                ProcessName = "stranger.exe",
                ProcessNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                ActivityId = foreignActivityId
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await CreateTrackingClient().PostAsJsonAsync(HeartbeatRoute, new DesktopActivityWindowDto
        {
            WindowStart = NineAm,
            Entries =
            [
                new DesktopActivityEntryDto
                {
                    ProcessName = "stranger.exe",
                    ProductName = "Stranger",
                    WindowTitle = "Stranger",
                    ExecutablePath = @"C:\test\stranger.exe",
                    IsFullscreen = false,
                    ActiveSeconds = 60,
                    BackgroundSeconds = 0,
                    IsPlayingSound = false,
                    ActiveMonitor = 0
                }
            ]
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the ingest path reports nothing wrong either way");

        await using var assertDb = CreateDbContext();

        var attributed = await assertDb.Set<ActivityHistory>()
            .IgnoreQueryFilters()
            .Where(h => h.ActivityId == foreignActivityId)
            .ToListAsync(CancellationToken);

        attributed.Should().ContainSingle(
            "SEC-2 residual: nothing on the ingest path re-checks that the mapping's target belongs to the " +
            "caller, so the seconds land on the stranger's activity");
        attributed[0].UserId.Should().Be(UserId,
            "the row is keyed on the caller, which is why the owner of the activity can never see it");
        attributed[0].Length.TotalSeconds.Should().Be(60);

        (await assertDb.Set<ActivityHistory>().IgnoreQueryFilters()
            .CountAsync(h => h.UserId == foreignUserId, CancellationToken))
            .Should().Be(0, "whatever else happens, the victim's own ledger is not written to");
    }

    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }
}
