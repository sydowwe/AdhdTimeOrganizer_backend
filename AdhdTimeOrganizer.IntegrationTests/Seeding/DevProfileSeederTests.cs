using System.Security.Claims;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// TEST-8 — the three <c>IPerUserDevSeeder</c>s that fill the profile tables, against a real database.
/// <para>
/// All three used to open with a <b>global</b> existence check (<c>AnyAsync()</c> with no predicate), so on a
/// dev database with two users the second one silently got nothing: the guard returns before the
/// <c>LogWarning</c> that would have said why, and the first user's data looks complete. That is exactly the
/// shape of database in which a cross-user scoping bug reads as working code (<c>CQ-3</c>, and see
/// <c>ActivityProfileGridTests</c> for what it hides). Nothing pinned it — these eight dev seeders had
/// registration coverage only.
/// </para>
/// <para>
/// The three profile entities are <b>not</b> <c>IEntityWithUser</c>, so "this user's rows" is only reachable
/// through <c>Activity.UserId</c>. Every assertion here goes through that traversal rather than through the
/// seeder's own guard, which is the thing under test.
/// </para>
/// <para>
/// <b>The host these run in has no ambient user at all</b>, which is the one thing this suite cannot
/// borrow from <see cref="PerUserDefaultSeederTests"/>. <c>FakeLoggedUserService.IsAuthenticated</c> is
/// hard-coded <c>true</c> and its id falls back to <c>TestUserId</c>, so every ordinary test context has a
/// non-null <c>ScopeUserId</c> — and <c>AppQueryFilters.ActivityProfileUserScope</c> would then hide the
/// other user's profiles from the seeder's own guard, masking exactly the bug being pinned. Dev seeding in
/// production runs outside a request, where <c>ScopeUserId</c> is null and the filter's
/// <c>ScopeUserId == null ||</c> escape (<c>MIG-1</c>) opens the table up. So the host below swaps in an
/// unauthenticated <c>ILoggedUserService</c> to reproduce that, which incidentally makes this the first
/// behavioural exercise of that escape rather than an assertion on the predicate.
/// </para>
/// <para>
/// Each seeder run gets a fresh DI scope, as <see cref="PerUserDefaultSeederTests"/> does: a second scope
/// sends a duplicate insert to the database instead of letting the first run's change tracker absorb it.
/// </para>
/// </summary>
[Collection("Postgres")]
public class DevProfileSeederTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Password = "Test@1234!";

    /// <summary>
    /// The lookups the four <c>userDefault</c> seeders own (orders 20-23). The dev seeders read them and seed
    /// nothing when they are absent, so a user has to be given them the way sign-up would.
    /// </summary>
    private static readonly string[] DefaultLookupSeeders =
        ["ActivityLocationType", "ActivityWeatherDependency", "ActivityExpectedCostTier", "ActivityExperienceType"];

    /// <summary>
    /// What each dev seeder is registered as, and which activities it recognises. The seeders match
    /// activities <b>by name</b>, so a user without these exact names gets zero rows and every assertion
    /// below would pass vacuously. Two names each is enough to tell "seeded" from "skipped".
    /// </summary>
    private sealed record SeederCase(Type EntityType, string[] ActivityNames);

    private static readonly Dictionary<string, SeederCase> Cases = new()
    {
        ["ActivityBacklogProfile"] = new(typeof(ActivityBacklogProfile), ["Gaming Session", "Meditation"]),
        ["ActivityProjectProfile"] = new(typeof(ActivityProjectProfile), ["Laundry", "Meal Preparation"]),
        ["ActivityBucketListProfile"] = new(typeof(ActivityBucketListProfile), ["Family Dinner", "Code Review"])
    };

    public static TheoryData<string> SeederNames => new(Cases.Keys);

    // ---- registration ------------------------------------------------------------------------------

    /// <summary>
    /// The names below are how every test here reaches its seeder, and how an operator filters one at the
    /// console. <c>ActivityProjectProfileSeeder</c> answered to <c>"ActivityDiyProfile"</c> long after the
    /// entity stopped being called that (<c>CQ-12</c>); a stale key is invisible until someone types it.
    /// </summary>
    [Fact]
    public void EachDevProfileSeeder_IsRegisteredOnce_UnderItsEntityName()
    {
        using var scope = NewScope();
        var names = scope.ServiceProvider.GetServices<IPerUserDevSeeder>()
            .Select(s => s.SeederName)
            .ToList();

        names.Should().Contain(Cases.Keys);
        names.Should().OnlyHaveUniqueItems("two seeders answering to one name makes Single() below ambiguous");
    }

    // ---- the defect: every user, not just the first --------------------------------------------------

    /// <summary>
    /// The regression itself. User B is seeded second and must get their own rows; before the fix the global
    /// guard saw user A's rows and returned, leaving B empty and silent.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task SeedingASecondUser_GivesThemTheirOwnRows_AndLeavesTheFirstUsersAlone(string seederName)
    {
        var probe = ProbeFor(Cases[seederName].EntityType);
        var (userIdA, activityIdsA) = await CreateSeededUserAsync(seederName);
        var (userIdB, activityIdsB) = await CreateSeededUserAsync(seederName);

        await RunDevSeederAsync(seederName, userIdA);

        List<long> anchoredOnA;
        await using (var db = CreateDbContext())
            anchoredOnA = await probe.ProfiledActivityIdsAsync(db, userIdA, CancellationToken);
        anchoredOnA.Should().Equal(activityIdsA, "the first user is the case that already worked");

        await RunDevSeederAsync(seederName, userIdB);

        await using var afterDb = CreateDbContext();
        (await probe.ProfiledActivityIdsAsync(afterDb, userIdB, CancellationToken))
            .Should().Equal(activityIdsB,
                "a global existence check would have seen user A's rows and returned before writing any of B's");
        (await probe.ProfiledActivityIdsAsync(afterDb, userIdA, CancellationToken))
            .Should().Equal(anchoredOnA, "user A's rows are not the seeder's to touch when it was told to seed B");
    }

    /// <summary>
    /// What the global guard was actually trying to buy, which the per-user guard must not lose: running a
    /// dev seeder twice over one user adds nothing and does not hit the unique <c>activity_id</c> index.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task SecondRun_ForTheSameUser_IsIdempotent(string seederName)
    {
        var probe = ProbeFor(Cases[seederName].EntityType);
        var (userId, activityIds) = await CreateSeededUserAsync(seederName);

        await RunDevSeederAsync(seederName, userId);

        var secondRun = () => RunDevSeederAsync(seederName, userId);
        await secondRun.Should().NotThrowAsync("ActivityId is unique per profile — a re-insert is a 23505");

        await using var afterDb = CreateDbContext();
        (await probe.ProfiledActivityIdsAsync(afterDb, userId, CancellationToken)).Should().Equal(activityIds);
    }

    /// <summary>
    /// A user with none of the named activities is the seeder's documented no-op: it logs and returns. It
    /// must not throw, and must not attribute rows to anybody else while it is at it.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task AUserWithNoActivities_IsSkippedWithoutThrowing(string seederName)
    {
        var probe = ProbeFor(Cases[seederName].EntityType);
        var userId = await CreateUserAsync();
        await RunDefaultLookupSeedersAsync(userId);

        var run = () => RunDevSeederAsync(seederName, userId);
        await run.Should().NotThrowAsync();

        await using var db = CreateDbContext();
        (await probe.ProfiledActivityIdsAsync(db, userId, CancellationToken)).Should().BeEmpty();
    }

    // ---- the lookups the dev seeders read but must never write ---------------------------------------

    /// <summary>
    /// <c>CQ-4</c>: the backlog and bucket-list seeders used to re-declare the lookup defaults and insert any
    /// they found missing — generically, so the <c>ActivityWeatherDependency</c> rows they created carried no
    /// <c>Code</c>, the one column <c>ActivityWeatherDependencySeeder</c> exists to set. They now read what
    /// the default seeders wrote and skip. A user missing the defaults is therefore a user who gets no
    /// profiles <b>and</b> no invented lookups, rather than a silently code-less set of them.
    /// </summary>
    [Theory]
    [InlineData("ActivityBacklogProfile")]
    [InlineData("ActivityBucketListProfile")]
    public async Task WithoutTheDefaultLookups_TheSeederWritesNothingAtAll(string seederName)
    {
        var probe = ProbeFor(Cases[seederName].EntityType);
        var userId = await CreateUserAsync();
        await SeedActivitiesAsync(userId, Cases[seederName].ActivityNames);

        var run = () => RunDevSeederAsync(seederName, userId);
        await run.Should().NotThrowAsync("a missing default is a skip with a warning, not a crash");

        await using var db = CreateDbContext();
        (await probe.ProfiledActivityIdsAsync(db, userId, CancellationToken)).Should().BeEmpty(
            "there is no lookup row to point a profile at");

        (await LookupCountAsync<ActivityWeatherDependency>(db, userId)).Should().Be(0,
            "a weather row invented here would carry no Code and fall back to the Infer heuristic the column removes");
        (await LookupCountAsync<ActivityLocationType>(db, userId)).Should().Be(0);
        (await LookupCountAsync<ActivityExpectedCostTier>(db, userId)).Should().Be(0);
        (await LookupCountAsync<ActivityExperienceType>(db, userId)).Should().Be(0);
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private ITestClientFactory? _unscopedHost;

    /// <summary>
    /// One host per test, built with no ambient user — see the class comment. Built lazily because two of
    /// the facts here never need it, and disposed with the test.
    /// </summary>
    private IServiceScope NewScope()
    {
        _unscopedHost ??= CreateFactory(roles: null, userId: null,
            configureServices: services => services.AddScoped<ILoggedUserService, NoAmbientUserService>());
        return _unscopedHost.Services.CreateScope();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_unscopedHost is not null)
            await _unscopedHost.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>
    /// <c>BaseDbContext.ScopeUserId</c> is <c>IsAuthenticated ? GetUserId : null</c>, so this is the only
    /// way to reach the null it documents as "apply no scoping": a background context, which is what a dev
    /// seeder runs in.
    /// </summary>
    private sealed class NoAmbientUserService : ILoggedUserService
    {
        public ClaimsPrincipal? LoggedUserPrincipal => null;
        public bool IsAuthenticated => false;
        public IEnumerable<string> GetRoles => [];
        public string GetEmail => throw new InvalidOperationException("No user is signed in.");
        public long GetUserId => throw new InvalidOperationException("No user is signed in.");
        public long? GetUserIdOrNull => null;
    }

    /// <summary>
    /// A user as dev seeding finds one: signed up (so the per-user defaults are in place) and carrying the
    /// activities this seeder matches by name. Returns those activity ids in insertion order, which is the
    /// order the assertions compare against.
    /// </summary>
    private async Task<(long UserId, List<long> ActivityIds)> CreateSeededUserAsync(string seederName)
    {
        var userId = await CreateUserAsync();
        await RunDefaultLookupSeedersAsync(userId);
        var activityIds = await SeedActivitiesAsync(userId, Cases[seederName].ActivityNames);
        return (userId, activityIds);
    }

    private async Task RunDevSeederAsync(string seederName, long userId)
    {
        using var scope = NewScope();
        var seeder = scope.ServiceProvider.GetServices<IPerUserDevSeeder>()
            .Single(s => s.SeederName == seederName);

        // Never TruncateTable() here: it is TRUNCATE ... CASCADE against the shared container database.
        await seeder.SeedForUser(userId);
    }

    private async Task RunDefaultLookupSeedersAsync(long userId)
    {
        using var scope = NewScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPerUserDefaultSeederManager>();
        foreach (var seederName in DefaultLookupSeeders)
            await manager.SeedForUserAsync(seederName, userId, false, CancellationToken);
    }

    private async Task<List<long>> SeedActivitiesAsync(long userId, IEnumerable<string> names)
    {
        await using var db = CreateDbContext();
        var ids = new List<long>();

        foreach (var name in names)
        {
            var role = new ActivityRole { UserId = userId, Name = $"{name} Role", Color = "#112233" };
            db.Set<ActivityRole>().Add(role);
            await db.SaveChangesAsync(CancellationToken);

            var activity = new Activity { UserId = userId, Name = name, RoleId = role.Id };
            db.Set<Activity>().Add(activity);
            await db.SaveChangesAsync(CancellationToken);
            ids.Add(activity.Id);
        }

        return ids.Order().ToList();
    }

    private async Task<long> CreateUserAsync()
    {
        var email = $"dev-seeder-{Guid.NewGuid():N}@test.com";
        using var scope = NewScope();
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

        // Deliberately not the registration endpoint: the defaults are run explicitly above, so a test can
        // also arrive at the user who has none of them.
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<int> LookupCountAsync<TLookup>(DbContext db, long userId) where TLookup : class, IEntityWithUser =>
        await db.Set<TLookup>().IgnoreQueryFilters().CountAsync(l => l.UserId == userId, CancellationToken);

    /// <summary>
    /// Reading a profile table for one user when the profile does not know who that is: the three entities
    /// are not <c>IEntityWithUser</c>, so ownership is a join through <see cref="Activity"/> every time.
    /// </summary>
    private interface IProfileProbe
    {
        Task<List<long>> ProfiledActivityIdsAsync(DbContext db, long userId, CancellationToken ct);
    }

    private sealed class ProfileProbe<TProfile> : IProfileProbe where TProfile : class, IActivityProfile
    {
        public async Task<List<long>> ProfiledActivityIdsAsync(DbContext db, long userId, CancellationToken ct)
        {
            var userActivityIds = db.Set<Activity>().IgnoreQueryFilters()
                .Where(a => a.UserId == userId)
                .Select(a => a.Id);

            return await db.Set<TProfile>().IgnoreQueryFilters()
                .Where(p => userActivityIds.Contains(p.ActivityId))
                .Select(p => p.ActivityId)
                .OrderBy(id => id)
                .ToListAsync(ct);
        }
    }

    private static IProfileProbe ProbeFor(Type entityType) =>
        (IProfileProbe)Activator.CreateInstance(typeof(ProfileProbe<>).MakeGenericType(entityType))!;
}
