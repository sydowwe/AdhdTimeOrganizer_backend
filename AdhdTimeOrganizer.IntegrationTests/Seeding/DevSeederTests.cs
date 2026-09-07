using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// CQ-11 — Core's three per-user <b>dev</b> seeders (<c>ActivityRole</c> 20, <c>ActivityCategory</c> 30,
/// <c>Activity</c> 40), which no test had ever executed.
/// </summary>
/// <remarks>
/// <para>
/// The behaviour under test is the truncate: <c>TRUNCATE … RESTART IDENTITY CASCADE</c> empties the
/// table for <b>every</b> user, while only the re-seed is per-user. That is correct for the dev-reset
/// flow — truncate every seeder in descending <c>Order</c>, then re-seed in ascending order — and the
/// docstrings now say so. Making the scope executable is what stops the old "already removed existing
/// data for this user" reading from coming back.
/// </para>
/// <para>
/// ⚠ These tests truncate shared tables, which the original prompt warned against. It is safe here for
/// one reason worth stating: <c>PostgresTestBase.InitializeAsync</c> Respawns the whole database before
/// <em>every</em> test and xUnit runs a collection's tests sequentially, so no other test's rows exist
/// while these run. Do not take this as licence to truncate from a test that assumes a populated
/// database.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class DevSeederTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    /// <summary>
    /// The finding itself, as an executable statement of scope. Reference:
    /// <c>Core/infrastructure/persistence/seeder/dev/ActivitySeeder.cs</c>'s <c>TruncateTable</c>.
    /// </summary>
    [Fact(DisplayName = "CQ-11: the dev seeder's truncate is app-wide, not per-user")]
    public async Task TruncateTable_EmptiesTheTableForEveryUser()
    {
        long mineId, theirsId;
        long otherUserId;
        await using (var db = CreateDbContext())
        {
            otherUserId = await ActivityMergeTestSupport.SecondUserAsync(Fixture, $"dev-seeder-{Guid.NewGuid():N}@test.com");
            mineId = await SeedActivityAsync(db, UserId, "Mine Before Truncate");
            theirsId = await SeedActivityAsync(db, otherUserId, "Theirs Before Truncate");
        }

        using (var scope = Fixture.AdminAndUserFactory.Services.CreateScope())
            await DevSeeder(scope, "Activity").TruncateTable();

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters()
            .AnyAsync(a => a.Id == mineId || a.Id == theirsId, CancellationToken))
            .Should().BeFalse("TRUNCATE ... CASCADE takes the whole table — only the re-seed that follows it is per-user");
    }

    /// <summary>
    /// Core's tables span three schemas (<c>user</c>, <c>activity</c>, <c>public</c>), and CLAUDE.md's
    /// rule is that hand-written SQL must name the schema or it resolves against the <c>search_path</c>.
    /// The truncate builds its statement from the EF model, so it is qualified — asserted through raw
    /// SQL naming <c>activity.activity</c> explicitly, because an EF read would resolve through the
    /// model either way and could not tell the two apart.
    /// </summary>
    [Fact(DisplayName = "CQ-11: the dev truncate names the schema, so it cannot hit search_path")]
    public async Task TruncateTable_IsSchemaQualified()
    {
        await using (var db = CreateDbContext())
            await SeedActivityAsync(db, UserId, "Schema Qualified Probe");

        await using (var beforeDb = CreateDbContext())
            (await CountActivityRowsAsync(beforeDb)).Should().Be(1, "the probe row must be visible in activity.activity first");

        using (var scope = Fixture.AdminAndUserFactory.Services.CreateScope())
            await DevSeeder(scope, "Activity").TruncateTable();

        await using var afterDb = CreateDbContext();
        (await CountActivityRowsAsync(afterDb)).Should().Be(0,
            "an unqualified TRUNCATE would either fail or empty some other schema's table, and the row would still be here");
    }

    /// <summary>
    /// Model-level, no execution. The three Orders encode a real FK chain — ActivityProfiles lookups
    /// 10–13 → role 20 → category 30 → activity 40 → profiles 50–52 → anchors 60 — and truncation
    /// reverses it, so a wrong number fails as an FK violation during a dev reset rather than as
    /// anything a compiler or a duplicate-registration check would see.
    /// <c>CoreRouteSmokeTests.CoreSeeders_AreRegisteredExactlyOnce</c> pins the other half.
    /// </summary>
    [Fact(DisplayName = "CQ-11: dev seeder Orders match the documented FK chain")]
    public void DevSeederOrders_MatchTheDocumentedBand()
    {
        using var scope = Fixture.AdminAndUserFactory.Services.CreateScope();

        DevSeeder(scope, "ActivityRole").Order.Should().Be(20);
        DevSeeder(scope, "ActivityCategory").Order.Should().Be(30, "a category must exist before the activity that classifies with it");
        DevSeeder(scope, "Activity").Order.Should().Be(40, "an activity needs its role and its category, so it seeds last of the three");

        foreach (var name in new[] { "ActivityRole", "ActivityCategory", "Activity" })
            DevSeeder(scope, name).Order.Should().BeInRange(10, 99,
                $"{name} sits in the 010-099 band SeederOrderBands.md assigns to Core and ActivityProfiles jointly");
    }

    /// <summary>
    /// A real branch, not a defensive one: <c>ActivityRoleSeeder</c> running out of order produces
    /// exactly this state, and the seeder's answer is a warning and an early return rather than a crash
    /// or a half-seeded user. Uses the suite's one log-capture harness.
    /// </summary>
    [Fact(DisplayName = "CQ-11: ActivitySeeder logs and returns when the user has no roles")]
    public async Task SeedForUser_WithNoRoles_LogsAWarningAndWritesNothing()
    {
        var provider = new CapturingLoggerProvider();
        await using var factory = Fixture.CreateFactory(null,
            configureServices: services => services.AddSingleton<ILoggerProvider>(provider));

        using (var scope = factory.Services.CreateScope())
            await DevSeeder(scope, "Activity").SeedForUser(UserId);

        provider.Messages.Should().Contain(m => m.Contains("No activity roles found", StringComparison.OrdinalIgnoreCase),
            "the skip has to be visible — a silent early return during a dev reset looks like the seeder ran");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<Activity>().IgnoreQueryFilters().AnyAsync(CancellationToken))
            .Should().BeFalse("without a role there is no valid Activity row to write, so it must write none at all");
    }

    // ---- helpers ----------------------------------------------------------------------------------

    private static IPerUserDevSeeder DevSeeder(IServiceScope scope, string seederName) =>
        scope.ServiceProvider.GetServices<IPerUserDevSeeder>().Single(s => s.SeederName == seederName);

    private async Task<long> CountActivityRowsAsync(DbContext db) =>
        await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM activity.activity")
            .SingleAsync(CancellationToken);

    private async Task<long> SeedActivityAsync(DbContext db, long userId, string name)
    {
        var role = new ActivityRole { UserId = userId, Name = $"{name} Role {Guid.NewGuid():N}", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = $"{name} {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }
}
