using System.Net;
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
/// The shape of <c>TrackerDesktopMappingByPattern.ActivityId</c> and its android twin: <b>many</b>
/// patterns to one activity, and <c>Cascade</c> on delete like every other activity reference.
/// </summary>
/// <remarks>
/// <para>
/// Both were <c>HasOne(...).WithOne()</c> until this was fixed, which put a unique index on
/// <c>activity_id</c>: a user could not map both <c>code.exe</c> and <c>rider64.exe</c> onto
/// "Programming", and the second create came back as a bare 409. Nothing in the matcher wanted that —
/// <c>DesktopActivityHeartbeatEndpoint</c> loads every active mapping and resolves a window with
/// <c>FirstOrDefault(MatchesPattern)</c>, never by activity. The real key is the pattern, which has its
/// own composite unique index per user.
/// </para>
/// <para>
/// And both were optional with no explicit <c>OnDelete</c>, so EF's <c>ClientSetNull</c> default left the
/// database constraint at <c>NO ACTION</c> — making these the only two activity FKs in the solution that
/// did not cascade. <c>DELETE /api/activity/{id}</c> on a mapped activity answered 409 with
/// "Foreign key constraint violation", naming no entity, and the user had no way to learn that a pattern
/// rule was the reason.
/// </para>
/// <para>
/// Both halves assert on <b>rows</b>, not on status codes alone: the whole family is configured by
/// convention helpers, so a regression here reappears as a silent index or a silent <c>NO ACTION</c>
/// rather than as a build error. <c>ActivityForeignKeyInventoryTests</c> is the model-level companion.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TrackerPatternMappingActivityFkTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    // ---- N:1 -------------------------------------------------------------------------------------

    /// <summary>
    /// Two desktop patterns mapped onto one activity, each written through its <b>own</b> DbContext.
    /// </summary>
    /// <remarks>
    /// The separate contexts are the point rather than incidental tidiness: they are the shape a second
    /// <c>POST</c> actually takes. Adding both through one context used to fail differently — EF's 1:1
    /// fixup nulled the <em>first</em> mapping's <c>ActivityId</c> in memory, so the save died on
    /// <c>CK_TrackerDesktopMappingByPattern_TargetRequired</c> and never reached the unique index at all.
    /// Neither failure exists now, but a test that only covered the one-context path would not notice the
    /// unique index coming back.
    /// </remarks>
    [Fact]
    public async Task TwoDesktopPatterns_CanTargetOneActivity()
    {
        long activityId;
        long firstId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Programming", ct: CancellationToken);
            firstId = await AddDesktopMappingAsync(db, activityId, "code");
        }

        await using (var db = CreateDbContext())
        {
            var secondId = await AddDesktopMappingAsync(db, activityId, "rider64");

            await using var assertDb = CreateDbContext();
            var mapped = await assertDb.Set<TrackerDesktopMappingByPattern>()
                .Where(m => m.ActivityId == activityId)
                .Select(m => m.Id)
                .ToListAsync(CancellationToken);

            mapped.Should().BeEquivalentTo([firstId, secondId],
                "activity_id must not be unique — a user maps several executables onto one activity");
        }
    }

    [Fact]
    public async Task TwoAndroidPatterns_CanTargetOneActivity()
    {
        long activityId;
        long firstId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Reading", ct: CancellationToken);
            firstId = await AddAndroidMappingAsync(db, activityId, "com.amazon.kindle");
        }

        await using (var db = CreateDbContext())
        {
            var secondId = await AddAndroidMappingAsync(db, activityId, "com.google.play.books");

            await using var assertDb = CreateDbContext();
            var mapped = await assertDb.Set<TrackerAndroidMappingByPattern>()
                .Where(m => m.ActivityId == activityId)
                .Select(m => m.Id)
                .ToListAsync(CancellationToken);

            mapped.Should().BeEquivalentTo([firstId, secondId]);
        }
    }

    // ---- Cascade ---------------------------------------------------------------------------------

    /// <summary>
    /// Deleting an activity destroys its pattern rules rather than being refused by them.
    /// </summary>
    /// <remarks>
    /// Archive (<c>PATCH /activity/{id}/archived</c>) is the non-destructive path; a hard delete means
    /// destroy, and the cascade is the point. Same shape as
    /// <c>ActivityEndpointTests.DeletingActivity_CascadesToItsPlannerTasks_NotRestrict</c>.
    /// </remarks>
    [Fact]
    public async Task DeletingActivity_CascadesToItsDesktopMapping_NotRestrict()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Mapped Activity", ct: CancellationToken);
        var mappingId = await AddDesktopMappingAsync(db, activityId, "doomed");

        var response = await CreateClient().DeleteAsync($"api/activity/{activityId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the FK is Cascade, not NO ACTION — a pattern rule must not make an activity undeletable");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters()
            .AnyAsync(m => m.Id == mappingId, CancellationToken))
            .Should().BeFalse("the mapping must be cascade-deleted along with its activity");
    }

    [Fact]
    public async Task DeletingActivity_CascadesToItsAndroidMapping_NotRestrict()
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Mapped Android Activity", ct: CancellationToken);
        var mappingId = await AddAndroidMappingAsync(db, activityId, "com.doomed.app");

        var response = await CreateClient().DeleteAsync($"api/activity/{activityId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<TrackerAndroidMappingByPattern>().IgnoreQueryFilters()
            .AnyAsync(m => m.Id == mappingId, CancellationToken))
            .Should().BeFalse();
    }

    // ---- seeding ---------------------------------------------------------------------------------

    // The check constraints pair each pattern column with its match type and require exactly one target
    // group, so both are set here — a pattern without its match type is rejected at the database.
    private async Task<long> AddDesktopMappingAsync(DbContext db, long activityId, string processName)
    {
        var mapping = new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = $"{processName}-{Guid.NewGuid():N}.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            ActivityId = activityId
        };
        db.Set<TrackerDesktopMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(CancellationToken);
        return mapping.Id;
    }

    private async Task<long> AddAndroidMappingAsync(DbContext db, long activityId, string packageName)
    {
        var mapping = new TrackerAndroidMappingByPattern
        {
            UserId = UserId,
            PackageName = $"{packageName}.{Guid.NewGuid():N}",
            PackageNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            ActivityId = activityId
        };
        db.Set<TrackerAndroidMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(CancellationToken);
        return mapping.Id;
    }
}
