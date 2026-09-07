using System.Security.Claims;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// TEST-13 — <c>RoutineTodoListSeeder</c>, the Routines slice's <c>IPerUserDevSeeder</c> (order 200).
/// <para>
/// The other routine seeder (<c>RoutineTimePeriodSeeder</c>) is covered by
/// <see cref="PerUserDefaultSeederTests"/> and <c>RoutineTimePeriodUniqueIndexTests</c>; this one had no
/// coverage at all. Everything it does is silent on failure: it matches nine activities <b>by name</b> and
/// three periods <b>by text</b>, and every miss is an <c>if</c> block that is simply skipped — rename an
/// activity in <c>ActivitySeeder</c> and a dev database quietly comes up with fewer routines, no error and
/// no log line beyond the final count. So the fixture below runs the <i>real</i> <c>ActivityRole</c> /
/// <c>Activity</c> dev seeders and the real <c>RoutineTimePeriod</c> default seeder rather than hand-seeding
/// the names it expects: an expectation written out here and a name written out there have to keep agreeing,
/// which is the whole point.
/// </para>
/// <para>
/// <b>Every run happens in a host with no ambient user</b>, like <see cref="DevProfileSeederTests"/> and for
/// the same reason: <c>FakeLoggedUserService.IsAuthenticated</c> is hard-coded <c>true</c>, so an ordinary
/// test host has <c>ScopeUserId == 999</c> and the global <c>IEntityWithUser</c> filter would hide the seeded
/// user's activities from the seeder's own lookups — it does not call <c>IgnoreQueryFilters</c>, unlike
/// <c>BasePerUserDefaultSeeder</c>. Dev seeding in production runs outside a request, where
/// <c>ScopeUserId</c> is null and the filter goes inert. Assertions read back through
/// <c>IgnoreQueryFilters</c> for the mirror-image reason: the fixture's own contexts <i>are</i> scoped to 999.
/// </para>
/// </summary>
[Collection("Postgres")]
public class RoutineDevSeederTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Password = "Test@1234!";

    private const string SeederName = "RoutineTodoList";
    private const string ActivityRoleSeederName = "ActivityRole";
    private const string ActivitySeederName = "Activity";
    private const string PeriodSeederName = "RoutineTimePeriod";

    /// <summary>
    /// The nine rows the seeder declares, as a literal — period text, activity name, step count. Copied from
    /// the seeder by hand on purpose: recomputing it from the seeder's own lookups would agree with a rename
    /// on either side of the match and assert nothing.
    /// </summary>
    private sealed record SeededRow(string Period, string Activity, int Steps);

    private static readonly SeededRow[] ExpectedRows =
    [
        new("Daily", "Morning Exercise", 3),
        new("Daily", "Meditation", 0),
        new("Daily", "Journaling", 0),
        new("Daily", "Meal Preparation", 3),
        new("Weekly", "Laundry", 0),
        new("Weekly", "House Cleaning", 4),
        new("Weekly", "Grocery Shopping", 0),
        new("Weekly", "Video Call Parents", 0),
        new("Monthly", "Read Technical Book", 0)
    ];

    // ---- the fixture the seeder is written against ---------------------------------------------------

    /// <summary>
    /// The shape and the count together. Nine rows across three of the user's four periods, with steps
    /// attached to exactly the three the seeder declares them on — an activity or period rename anywhere in
    /// the seeding chain lands here as a missing row rather than as a smaller number nobody reads.
    /// </summary>
    [Fact]
    public async Task SeedForUser_WithTheStandardActivityFixture_SeedsTheExpectedRows()
    {
        var userId = await CreateFixtureUserAsync();

        await RunDevSeederAsync(SeederName, userId);

        var rows = await SeededRowsAsync(userId);

        rows.Should().BeEquivalentTo(ExpectedRows);
        rows.Where(r => r.Period == "Daily").Should().HaveCount(4);
        rows.Where(r => r.Period == "Weekly").Should().HaveCount(4);
        rows.Where(r => r.Period == "Monthly").Should().HaveCount(1);
        rows.Should().NotContain(r => r.Period == "Yearly",
            "the user has a Yearly period from the default seeder and the dev seeder declares nothing for it");
    }

    /// <summary>
    /// The two documented early returns (<c>:41-51</c>). Neither is an error — a user without the dev
    /// activity fixture simply gets no routines — but both have to stay quiet rather than throw, because dev
    /// seeding runs the whole chain for every user in one pass.
    /// </summary>
    [Fact]
    public async Task SeedForUser_WithNoActivities_SkipsCleanlyAndLogsAWarning()
    {
        var userId = await CreateUserAsync();
        await RunDefaultSeederAsync(PeriodSeederName, userId);

        var run = () => RunDevSeederAsync(SeederName, userId);
        await run.Should().NotThrowAsync();

        (await SeededRowsAsync(userId)).Should().BeEmpty();
        _logs.Messages.Should().Contain(m => m.Contains("No activities found for user"),
            "the count line at the end is otherwise the only trace, and it reads the same as a successful run of zero rows");
    }

    [Fact]
    public async Task SeedForUser_WithNoTimePeriods_SkipsCleanly()
    {
        var userId = await CreateUserAsync();
        await RunDevSeederAsync(ActivityRoleSeederName, userId);
        await RunDevSeederAsync(ActivitySeederName, userId);

        var run = () => RunDevSeederAsync(SeederName, userId);
        await run.Should().NotThrowAsync();

        (await SeededRowsAsync(userId)).Should().BeEmpty();
    }

    /// <summary>
    /// The three <c>nextOrder*</c> counters are independent (<c>:75-85</c>, then a decrement per row): each
    /// period is its own descending sequence starting at <c>DisplayOrderStart</c>, because the list the user
    /// sees is one list per period. A single shared counter would still look sorted within each period and
    /// would only show up as periods whose first item does not start at the top.
    /// </summary>
    [Fact]
    public async Task SeedForUser_AssignsDescendingDisplayOrderWithinEachPeriod()
    {
        var userId = await CreateFixtureUserAsync();
        var settings = SettingsValue();
        var start = settings.DisplayOrderStart;
        var gap = settings.DisplayOrderGap;

        await RunDevSeederAsync(SeederName, userId);

        var orders = await DisplayOrdersByPeriodAsync(userId);

        orders["Daily"].Should().Equal(new[] { start, start - gap, start - 2 * gap, start - 3 * gap });
        orders["Weekly"].Should().Equal(new[] { start, start - gap, start - 2 * gap, start - 3 * gap },
            "the weekly counter is its own — it must start at the top, not continue where the daily one stopped");
        orders["Monthly"].Should().Equal(new[] { start });

        foreach (var (period, values) in orders)
        {
            values.Should().OnlyHaveUniqueItems($"two {period} rows sharing a display order have no defined order");
            values.Should().BeInDescendingOrder();
        }
    }

    /// <summary>
    /// <c>(user_id, time_period_id, activity_id)</c> is unique, so truncate-then-seed is not a convention but
    /// the only order that works. The seeder says so in a comment (<c>:29</c>) and nothing enforced it: a
    /// re-seed without the truncate has to fail loudly rather than double every routine.
    /// </summary>
    [Fact]
    public async Task SeedForUser_TwiceWithoutTruncate_ViolatesTheUniqueIndex()
    {
        var userId = await CreateFixtureUserAsync();
        await RunDevSeederAsync(SeederName, userId);

        var secondRun = () => RunDevSeederAsync(SeederName, userId);
        await secondRun.Should().ThrowAsync<DbUpdateException>();

        (await SeededRowsAsync(userId)).Should().BeEquivalentTo(ExpectedRows,
            "the failed insert is one SaveChanges, so it rolls back whole and leaves the first run's rows alone");
    }

    /// <summary>
    /// <b>This is the intended dev-seeder contract, not a bug — do not "fix" it into a per-user delete.</b>
    /// The dev-reset flow truncates every seeder in descending <c>Order</c> first and only then re-seeds per
    /// user, so <c>TruncateTable</c> is app-wide by design even though the seeder is per-user. It is also the
    /// single most destructive statement in the module, it bypasses the ChangeTracker entirely, and its blast
    /// radius is wider than its name: <c>TRUNCATE … CASCADE</c> also empties every table holding a foreign key
    /// into <c>routine_todo_list</c>, which today means <c>activity_history</c>
    /// (<c>ActivityHistory.RoutineTodoListId</c>, declared in <c>AppDbContext.ConfigureCrossSliceRelationships</c>).
    /// It stops there — the two tables the routine cycle is reconstructed from, <c>routine_time_period</c> and
    /// <c>routine_period_completions</c>, are referenced <i>by</i> routine items rather than referencing them,
    /// and survive.
    /// <para>
    /// The schema qualification the statement needs is pinned separately by
    /// <c>ModuleSchemaTests.TruncateFindsTheTableInItsOwnSchema</c>; unqualified, this would be a 42P01.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TruncateTable_RemovesEveryUsersRoutineItems()
    {
        var userIdA = await CreateFixtureUserAsync();
        var userIdB = await CreateFixtureUserAsync();
        await RunDevSeederAsync(SeederName, userIdA);
        await RunDevSeederAsync(SeederName, userIdB);

        await SeedCascadeProbesAsync(userIdA);

        (await SeededRowsAsync(userIdA)).Should().HaveCount(ExpectedRows.Length);
        (await SeededRowsAsync(userIdB)).Should().HaveCount(ExpectedRows.Length);

        await TruncateAsync();

        (await SeededRowsAsync(userIdA)).Should().BeEmpty();
        (await SeededRowsAsync(userIdB)).Should().BeEmpty(
            "a dev seeder's truncate is app-wide on purpose — the reset flow truncates every table before it re-seeds anybody");

        await using var db = CreateDbContext();
        (await db.Set<RoutineTimePeriod>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().BeGreaterThan(0, "periods are what the items point at, not the other way round");
        (await db.Set<RoutinePeriodCompletion>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().BeGreaterThan(0, "the completion ledger hangs off the period, so the cascade does not reach it");
        (await db.Set<ActivityHistory>().IgnoreQueryFilters().CountAsync(CancellationToken))
            .Should().Be(0,
                "CASCADE truncates every table with a FK into routine_todo_list, and activity_history has one — " +
                "recorded time is collateral damage of re-seeding routines, which is worth knowing before it happens");
    }

    // ---- fixture ------------------------------------------------------------------------------------

    /// <summary>
    /// A user as dev seeding finds one: the eight roles and nineteen activities the two Core dev seeders
    /// write, plus the four routine periods sign-up would have given them. Run through the real seeders, so
    /// the names this suite expects are the names those seeders actually produce.
    /// </summary>
    private async Task<long> CreateFixtureUserAsync()
    {
        var userId = await CreateUserAsync();
        await RunDevSeederAsync(ActivityRoleSeederName, userId);
        await RunDevSeederAsync(ActivitySeederName, userId);
        await RunDefaultSeederAsync(PeriodSeederName, userId);
        return userId;
    }

    /// <summary>One row in each of the two tables the truncate must not reach, and one it does.</summary>
    private async Task SeedCascadeProbesAsync(long userId)
    {
        await using var db = CreateDbContext();

        var period = await db.Set<RoutineTimePeriod>().IgnoreQueryFilters()
            .FirstAsync(p => p.UserId == userId && p.Text == "Daily", CancellationToken);
        var item = await db.Set<RoutineTodoList>().IgnoreQueryFilters()
            .FirstAsync(r => r.UserId == userId, CancellationToken);

        db.Set<RoutinePeriodCompletion>().Add(new RoutinePeriodCompletion
        {
            TimePeriodId = period.Id,
            PeriodStart = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1),
            PeriodEnd = DateOnly.FromDateTime(DateTime.UtcNow),
            CompletedCount = 1,
            TotalCount = 1
        });

        // Recorded against the routine item, which is what puts activity_history inside the cascade.
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = userId,
            ActivityId = item.ActivityId,
            RoutineTodoListId = item.Id,
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-30),
            Length = new IntTime(0, 30)
        });

        await db.SaveChangesAsync(CancellationToken);
    }

    // ---- reading back --------------------------------------------------------------------------------

    /// <summary>
    /// The seeded rows as (period text, activity name, step count). Both ids are resolved back to the names
    /// the seeder matched on, since an id tells you nothing about which <c>if</c> block ran.
    /// </summary>
    private async Task<List<SeededRow>> SeededRowsAsync(long userId)
    {
        await using var db = CreateDbContext();

        var items = await db.Set<RoutineTodoList>().IgnoreQueryFilters()
            .Where(r => r.UserId == userId)
            .ToListAsync(CancellationToken);
        var periods = await PeriodTextsAsync(db, userId);
        var activities = await db.Set<Activity>().IgnoreQueryFilters()
            .Where(a => a.UserId == userId)
            .ToDictionaryAsync(a => a.Id, a => a.Name, CancellationToken);

        // Steps are an owned ToJson collection on the same row, so they arrive without an Include.
        return items
            .Select(i => new SeededRow(periods[i.TimePeriodId], activities[i.ActivityId], i.Steps.Count))
            .ToList();
    }

    private async Task<Dictionary<string, List<long>>> DisplayOrdersByPeriodAsync(long userId)
    {
        await using var db = CreateDbContext();

        var items = await db.Set<RoutineTodoList>().IgnoreQueryFilters()
            .Where(r => r.UserId == userId)
            .Select(r => new { r.TimePeriodId, r.DisplayOrder })
            .ToListAsync(CancellationToken);
        var periods = await PeriodTextsAsync(db, userId);

        return items
            .GroupBy(i => periods[i.TimePeriodId])
            .ToDictionary(g => g.Key, g => g.Select(i => i.DisplayOrder).OrderByDescending(o => o).ToList());
    }

    private Task<Dictionary<long, string>> PeriodTextsAsync(DbContext db, long userId) =>
        db.Set<RoutineTimePeriod>().IgnoreQueryFilters()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Id, p => p.Text, CancellationToken);

    // ---- running the seeders --------------------------------------------------------------------------

    private async Task RunDevSeederAsync(string seederName, long userId)
    {
        using var scope = NewScope();
        var seeder = scope.ServiceProvider.GetServices<IPerUserDevSeeder>().Single(s => s.SeederName == seederName);
        await seeder.SeedForUser(userId);
    }

    /// <summary>
    /// The one place <c>TruncateTable</c> is called. Every other test seeds only its own users and leaves the
    /// shared container database alone — this is <c>TRUNCATE … CASCADE</c>, and the Postgres collection runs
    /// sequentially, but the respawn between tests is what actually keeps it contained.
    /// </summary>
    private async Task TruncateAsync()
    {
        using var scope = NewScope();
        var seeder = scope.ServiceProvider.GetServices<IPerUserDevSeeder>().Single(s => s.SeederName == SeederName);
        await seeder.TruncateTable();
    }

    private async Task RunDefaultSeederAsync(string seederName, long userId)
    {
        using var scope = NewScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPerUserDefaultSeederManager>();
        await manager.SeedForUserAsync(seederName, userId, false, CancellationToken);
    }

    private TodoListSettings SettingsValue()
    {
        using var scope = NewScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<TodoListSettings>>().Value;
    }

    private async Task<long> CreateUserAsync()
    {
        var email = $"routine-dev-seeder-{Guid.NewGuid():N}@test.com";
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

        // Deliberately not the registration endpoint: the per-user defaults are run explicitly above, so a
        // test can also arrive at the user who has none of them.
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    // ---- host ----------------------------------------------------------------------------------------

    private readonly CapturingLoggerProvider _logs = new();
    private ITestClientFactory? _seedingHost;

    /// <summary>
    /// One host per test — no ambient user (see the class comment) and a capturing log provider, since two of
    /// this seeder's three outcomes are log lines rather than rows.
    /// </summary>
    private IServiceScope NewScope()
    {
        _seedingHost ??= CreateFactory(roles: null, userId: null, configureServices: services =>
        {
            services.AddScoped<ILoggedUserService, NoAmbientUserService>();
            // SerilogConfig passes writeToProviders: true, so Serilog forwards to any registered provider.
            services.AddSingleton<ILoggerProvider>(_logs);
        });
        return _seedingHost.Services.CreateScope();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_seedingHost is not null)
            await _seedingHost.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>
    /// <c>BaseDbContext.ScopeUserId</c> is <c>IsAuthenticated ? GetUserId : null</c>, so this is the only way
    /// to reach the null it documents as "apply no scoping" — a background context, which is what a dev
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

    /// <summary>Captures every formatted log message across all categories, for asserting on presence.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                    return _messages.ToList();
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);
        public void Dispose() { }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                    messages.Add(formatter(state, exception));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();
                public void Dispose() { }
            }
        }
    }
}
