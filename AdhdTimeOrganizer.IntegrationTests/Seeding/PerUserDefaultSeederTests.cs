using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface.manager;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// TEST-18 — the concrete <c>IPerUserDefaultSeeder</c>s, against a real database.
/// <para>
/// <c>PerUserDefaultMatcherTests</c> pins the shared matching logic in isolation; nothing pinned the
/// twelve seeders that <i>call</i> it. A seeder can hand the matcher the wrong key selector (matching
/// on the display text when the unique index is on a number, say) and still look correct in review —
/// the matcher does exactly what it is told, and the 23505 lands at the seeder. So every assertion
/// here is on rows in the database after running the seeder through its real entry point
/// (<see cref="IPerUserDefaultSeederManager.SeedForUserAsync"/>), never on the matcher.
/// </para>
/// <para>
/// <b>Every run gets a fresh DI scope</b>, because that is what production does — sign-up seeds in one
/// request scope and a replay happens in another. Reusing one scope would leave the first run's rows
/// tracked by the same <c>DbContext</c>, which can mask a duplicate insert that a second scope would
/// send to the database and have rejected.
/// </para>
/// <para>
/// Seeders are resolved from the <b>unauthenticated</b> factory: <c>ScopeUserId</c> is then null, the
/// global <c>IEntityWithUser</c> filter is a no-op, and seeding user B is not implicitly scoped to
/// whoever happens to be signed in — the same reason <c>BasePerUserDefaultSeeder</c> calls
/// <c>IgnoreQueryFilters</c>.
/// </para>
/// </summary>
[Collection("Postgres")]
public class PerUserDefaultSeederTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Password = "Test@1234!";

    /// <summary>
    /// What each seeder owns and what it must produce. Written out here rather than read back off the
    /// seeder's own <c>Defaults</c> — an expectation copied from the thing under test asserts nothing.
    /// </summary>
    private sealed record SeederCase(Type EntityType, int ExpectedCount, string? KeyProperty = null, string[]? ExpectedKeys = null);

    private static readonly Dictionary<string, SeederCase> Cases = new()
    {
        // Core
        ["DefaultActivityRole"] = new(typeof(ActivityRole), 3, nameof(ActivityRole.Name),
            ["Planner task", "To-do list task", "Routine task"]),
        ["TimerPreset"] = new(typeof(TimerPreset), 7),
        ["PomodoroTimerPreset"] = new(typeof(PomodoroTimerPreset), 4, nameof(PomodoroTimerPreset.Name),
            ["Classic Pomodoro", "Extended Focus", "Short Sprint", "Deep Work"]),

        // ActivityProfiles
        ["ActivityLocationType"] = new(typeof(ActivityLocationType), 3, nameof(ActivityLocationType.Text),
            ["Indoor", "Outdoor", "Any"]),
        ["ActivityWeatherDependency"] = new(typeof(ActivityWeatherDependency), 4, nameof(ActivityWeatherDependency.Text),
            ["None", "Sunny", "Dry", "Snow"]),
        ["ActivityExpectedCostTier"] = new(typeof(ActivityExpectedCostTier), 4, nameof(ActivityExpectedCostTier.Text),
            ["Free", "Cheap", "Moderate", "Expensive"]),
        ["ActivityExperienceType"] = new(typeof(ActivityExperienceType), 5, nameof(ActivityExperienceType.Text),
            ["Adrenaline", "Travel", "Skill", "Culinary", "Cultural"]),

        // TodoLists / Routines
        ["TaskPriority"] = new(typeof(TaskPriority), 4, nameof(TaskPriority.Text),
            ["Today", "This week", "This month", "This year"]),
        ["RoutineTimePeriod"] = new(typeof(RoutineTimePeriod), 4, nameof(RoutineTimePeriod.Text),
            ["Daily", "Weekly", "Monthly", "Yearly"]),

        // Planning (Calendar has its own section — it is not a BasePerUserDefaultSeeder)
        ["TaskImportance"] = new(typeof(TaskImportance), 2, nameof(TaskImportance.Text), ["Critical", "Optional"]),
        ["UserPlannerSettings"] = new(typeof(UserPlannerSettings), 1)
    };

    private const string CalendarSeederName = "Calendar";

    public static TheoryData<string> SeederNames => new(Cases.Keys);

    // ---- registration ------------------------------------------------------------------------------

    /// <summary>
    /// The set below is what the rest of this file asserts against, so it has to be the set the host
    /// actually resolves. Per CLAUDE.md an assembly scan anchored on a type that moved slices drops
    /// seeders silently — a dropped seeder would otherwise just stop being tested, not start failing.
    /// </summary>
    [Fact]
    public void EveryPerUserDefaultSeeder_IsRegistered_ExactlyOnce()
    {
        using var scope = NewScope();
        var names = scope.ServiceProvider.GetRequiredService<IPerUserDefaultSeederManager>()
            .GetAllSeederNames()
            .ToList();

        names.Should().BeEquivalentTo([.. Cases.Keys, CalendarSeederName],
            "a seeder missing here was dropped by an assembly scan; an extra one needs a case adding to this file");
        names.Should().OnlyHaveUniqueItems(
            "SeederResolver dedupes by concrete type, so two names surviving means two different seeders claim one name");
    }

    // ---- the shared contract, per seeder ------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task FirstRun_InsertsExactlyTheDefaults_ForThatUserAlone(string seederName)
    {
        var (entityType, expectedCount, keyProperty, expectedKeys) = Cases[seederName];
        var probe = ProbeFor(entityType);
        var userId = await CreateUserAsync();

        await RunSeederAsync(seederName, userId);

        await using var db = CreateDbContext();
        var rows = await probe.RowsForUserAsync(db, userId, CancellationToken);

        rows.Should().HaveCount(expectedCount);
        (await probe.AllRowsAsync(db, CancellationToken)).Should().HaveCount(expectedCount,
            "the seeder must not have written rows attributed to anyone else");

        if (keyProperty is not null)
            rows.Select(r => Get(r, keyProperty)).Should().BeEquivalentTo(expectedKeys);
    }

    /// <summary>
    /// The regression class the matcher was hardened against, exercised per seeder: a second run must
    /// insert nothing and must not hit a unique index. A wrong key selector passes the isolated matcher
    /// test and fails here.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task SecondRun_ForTheSameUser_IsIdempotent_AndDoesNotViolateAUniqueIndex(string seederName)
    {
        var (entityType, expectedCount, _, _) = Cases[seederName];
        var probe = ProbeFor(entityType);
        var userId = await CreateUserAsync();

        await RunSeederAsync(seederName, userId);

        List<long> idsAfterFirst;
        await using (var db = CreateDbContext())
            idsAfterFirst = (await probe.RowsForUserAsync(db, userId, CancellationToken)).Select(IdOf).Order().ToList();

        var secondRun = () => RunSeederAsync(seederName, userId);
        await secondRun.Should().NotThrowAsync("re-seeding an already-seeded user is the sign-up-replay path, not an error");

        await using var afterDb = CreateDbContext();
        var rows = await probe.RowsForUserAsync(afterDb, userId, CancellationToken);
        rows.Should().HaveCount(expectedCount);
        rows.Select(IdOf).Order().Should().Equal(idsAfterFirst, "nothing was missing, so nothing should have been inserted");
    }

    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task SeedingUserB_LeavesUserARowsUntouched(string seederName)
    {
        var (entityType, expectedCount, _, _) = Cases[seederName];
        var probe = ProbeFor(entityType);
        var userIdA = await CreateUserAsync();
        var userIdB = await CreateUserAsync();

        await RunSeederAsync(seederName, userIdA);

        List<long> idsOfA;
        await using (var db = CreateDbContext())
            idsOfA = (await probe.RowsForUserAsync(db, userIdA, CancellationToken)).Select(IdOf).Order().ToList();

        await RunSeederAsync(seederName, userIdB);

        await using var afterDb = CreateDbContext();
        (await probe.RowsForUserAsync(afterDb, userIdA, CancellationToken)).Select(IdOf).Order()
            .Should().Equal(idsOfA, "user A's rows are not the seeder's to touch when it was told to seed user B");
        (await probe.RowsForUserAsync(afterDb, userIdB, CancellationToken)).Should().HaveCount(expectedCount);
        (await probe.AllRowsAsync(afterDb, CancellationToken)).Should().HaveCount(expectedCount * 2);
    }

    /// <summary>
    /// The partially-seeded user: deleting one default must refill exactly that one. Counting rows
    /// instead of matching keys is what used to re-insert the whole set onto a live unique index.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task AfterTheUserDeletesOneDefault_OnlyTheGapIsRefilled(string seederName)
    {
        var (entityType, expectedCount, _, _) = Cases[seederName];
        var probe = ProbeFor(entityType);
        var userId = await CreateUserAsync();

        await RunSeederAsync(seederName, userId);

        List<long> survivingIds;
        await using (var db = CreateDbContext())
        {
            var rows = await probe.RowsForUserAsync(db, userId, CancellationToken);
            var doomed = rows.OrderBy(IdOf).Last();
            db.Remove(doomed);
            await db.SaveChangesAsync(CancellationToken);
            survivingIds = rows.Select(IdOf).Where(id => id != IdOf(doomed)).Order().ToList();
        }

        var refill = () => RunSeederAsync(seederName, userId);
        await refill.Should().NotThrowAsync();

        await using var afterDb = CreateDbContext();
        var afterRows = await probe.RowsForUserAsync(afterDb, userId, CancellationToken);
        afterRows.Should().HaveCount(expectedCount, "the deleted default comes back");

        // UserPlannerSettings seeds a single row, so there is nothing left to survive its deletion.
        if (survivingIds.Count > 0)
            afterRows.Select(IdOf).Should().Contain(survivingIds,
                "the rows the user kept must survive with their ids — anything referencing them still points at them");
    }

    /// <summary>
    /// Reset rewrites in place. Row ids surviving is the whole point of <c>ResetDefaults</c> existing
    /// separately from a truncate-and-reinsert: activities, tasks and profiles hang off these ids.
    /// </summary>
    [Theory]
    [MemberData(nameof(SeederNames))]
    public async Task Reset_RewritesInPlace_KeepingEveryRowId(string seederName)
    {
        var (entityType, expectedCount, _, _) = Cases[seederName];
        var probe = ProbeFor(entityType);
        var userId = await CreateUserAsync();

        await RunSeederAsync(seederName, userId);

        List<long> idsBefore;
        await using (var db = CreateDbContext())
            idsBefore = (await probe.RowsForUserAsync(db, userId, CancellationToken)).Select(IdOf).Order().ToList();

        var reset = () => RunSeederAsync(seederName, userId, overrideData: true);
        await reset.Should().NotThrowAsync();

        await using var afterDb = CreateDbContext();
        var rows = await probe.RowsForUserAsync(afterDb, userId, CancellationToken);
        rows.Should().HaveCount(expectedCount);
        rows.Select(IdOf).Order().Should().Equal(idsBefore);
    }

    // ---- key selector: the seeder-specific half the matcher cannot get wrong on its own -------------

    /// <summary>
    /// Each of these four seeders documents a unique index that is <i>not</i> the display text —
    /// <c>(user_id, name)</c> for activity roles, <c>(user_id, priority)</c>, <c>(user_id, importance)</c>,
    /// and both <c>(user_id, text)</c> and <c>(user_id, length_in_days)</c> for routine time periods.
    /// Renaming a seeded row is therefore the case that separates a correct <c>Collides</c> from one
    /// keyed on the text: keyed on the text, the renamed row looks missing, gets re-inserted, and the
    /// real index rejects it with a 23505.
    /// </summary>
    [Theory]
    [InlineData("DefaultActivityRole", nameof(ActivityRole.Text), "Quickly created activities in task planner", "My own description")]
    [InlineData("TaskPriority", nameof(TaskPriority.Text), "Today", "Dnes")]
    [InlineData("TaskImportance", nameof(TaskImportance.Text), "Critical", "Urgent")]
    [InlineData("RoutineTimePeriod", nameof(RoutineTimePeriod.Text), "Daily", "Every day")]
    public async Task RenamingASeededRow_DoesNotMakeItLookMissing(
        string seederName, string property, string original, string renamed)
    {
        var (entityType, expectedCount, _, _) = Cases[seederName];
        var probe = ProbeFor(entityType);
        var userId = await CreateUserAsync();

        await RunSeederAsync(seederName, userId);
        var renamedId = await RenameAsync(probe, userId, property, original, renamed);

        var reseed = () => RunSeederAsync(seederName, userId);
        await reseed.Should().NotThrowAsync(
            "a renamed default is still that default — re-inserting it would collide with the unique index it is keyed on");

        await using var db = CreateDbContext();
        var rows = await probe.RowsForUserAsync(db, userId, CancellationToken);
        rows.Should().HaveCount(expectedCount, "the rename must not have produced a second copy of the row");
        Get(rows.Single(r => IdOf(r) == renamedId), property).Should().Be(renamed,
            "plain setup never overwrites what the user changed — only an explicit reset does");
    }

    [Theory]
    [InlineData("DefaultActivityRole", nameof(ActivityRole.Text), "Quickly created activities in task planner", "My own description")]
    [InlineData("TaskPriority", nameof(TaskPriority.Text), "Today", "Dnes")]
    [InlineData("TaskImportance", nameof(TaskImportance.Text), "Critical", "Urgent")]
    [InlineData("RoutineTimePeriod", nameof(RoutineTimePeriod.Text), "Daily", "Every day")]
    public async Task Reset_RestoresARenamedRow_OnItsOwnId(
        string seederName, string property, string original, string renamed)
    {
        var (entityType, expectedCount, _, _) = Cases[seederName];
        var probe = ProbeFor(entityType);
        var userId = await CreateUserAsync();

        await RunSeederAsync(seederName, userId);
        var renamedId = await RenameAsync(probe, userId, property, original, renamed);

        await RunSeederAsync(seederName, userId, overrideData: true);

        await using var db = CreateDbContext();
        var rows = await probe.RowsForUserAsync(db, userId, CancellationToken);
        rows.Should().HaveCount(expectedCount);
        Get(rows.Single(r => IdOf(r) == renamedId), property).Should().Be(original,
            "reset must land the default back on the row that already carries its key, not on a new row");
    }

    // ---- UserPlannerSettings: one row per user, keyed on nothing but the user -----------------------

    /// <summary>
    /// <c>Collides</c> returns <c>true</c> unconditionally here — the user id alone is the key. That is
    /// only correct if the table really is one row per user, so this is the fact that says so: a second
    /// run must not add a second settings row, and must not overwrite what the user changed.
    /// </summary>
    [Fact]
    public async Task UserPlannerSettings_SecondRun_NeitherDuplicatesTheRowNorOverwritesTheUsersEdits()
    {
        var userId = await CreateUserAsync();
        await RunSeederAsync("UserPlannerSettings", userId);

        long settingsId;
        await using (var db = CreateDbContext())
        {
            var settings = await SettingsOf(db, userId);
            settingsId = settings.Id;
            settings.RemindersEnabled = false;
            settings.ReminderMinutesBefore = 45;
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunSeederAsync("UserPlannerSettings", userId);

        await using var afterDb = CreateDbContext();
        var reloaded = await SettingsOf(afterDb, userId);
        reloaded.Id.Should().Be(settingsId);
        reloaded.RemindersEnabled.Should().BeFalse();
        reloaded.ReminderMinutesBefore.Should().Be(45);
    }

    [Fact]
    public async Task UserPlannerSettings_Reset_RestoresTheDefaultsOnTheSameRow()
    {
        var userId = await CreateUserAsync();
        await RunSeederAsync("UserPlannerSettings", userId);

        long settingsId;
        await using (var db = CreateDbContext())
        {
            var settings = await SettingsOf(db, userId);
            settingsId = settings.Id;
            settings.RemindersEnabled = false;
            settings.ReminderMinutesBefore = 45;
            settings.ArrowKeyNavEnabled = false;
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunSeederAsync("UserPlannerSettings", userId, overrideData: true);

        await using var afterDb = CreateDbContext();
        var reloaded = await SettingsOf(afterDb, userId);
        reloaded.Id.Should().Be(settingsId, "the planner reads its settings by this id");
        reloaded.RemindersEnabled.Should().BeTrue();
        reloaded.ReminderMinutesBefore.Should().Be(10);
        reloaded.ArrowKeyNavEnabled.Should().BeTrue();
    }

    private async Task<UserPlannerSettings> SettingsOf(DbContext db, long userId) =>
        await db.Set<UserPlannerSettings>().IgnoreQueryFilters().SingleAsync(s => s.UserId == userId, CancellationToken);

    // ---- CalendarSeeder: the one that is not a BasePerUserDefaultSeeder ------------------------------

    /// <summary>
    /// It fills whole years rather than a fixed list, so none of the shared cases above apply to it —
    /// and the rolling window (this year and next, resolved at run time) is the part with a history:
    /// the years used to be hard-coded, which set an expiry date on the planner.
    /// </summary>
    [Fact]
    public async Task Calendar_FirstRun_FillsThisYearAndNext_ForThatUserAlone()
    {
        var userId = await CreateUserAsync();
        var otherUserId = await CreateUserAsync();
        var thisYear = DateTime.UtcNow.Year;

        await RunSeederAsync(CalendarSeederName, userId);

        await using var db = CreateDbContext();
        var dates = await CalendarDatesOf(db, userId);

        dates.Should().HaveCount(DaysIn(thisYear) + DaysIn(thisYear + 1));
        dates.First().Should().Be(new DateOnly(thisYear, 1, 1));
        dates.Last().Should().Be(new DateOnly(thisYear + 1, 12, 31));
        dates.Should().OnlyHaveUniqueItems("(user_id, date) is unique");

        (await CalendarDatesOf(db, otherUserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Calendar_SecondRun_AddsNothing()
    {
        var userId = await CreateUserAsync();
        await RunSeederAsync(CalendarSeederName, userId);

        List<long> idsAfterFirst;
        await using (var db = CreateDbContext())
            idsAfterFirst = await CalendarIdsOf(db, userId);

        var secondRun = () => RunSeederAsync(CalendarSeederName, userId);
        await secondRun.Should().NotThrowAsync();

        await using var afterDb = CreateDbContext();
        (await CalendarIdsOf(afterDb, userId)).Should().Equal(idsAfterFirst);
    }

    /// <summary>
    /// The reason the seeder checks per missing date rather than "does this year have any rows at all":
    /// a year the user has partly deleted has to be filled in, and neither skipped whole (the day stays
    /// missing) nor re-inserted whole (every surviving day is a 23505).
    /// </summary>
    [Fact]
    public async Task Calendar_AfterTheUserDeletesOneDay_RefillsOnlyThatDay()
    {
        var userId = await CreateUserAsync();
        await RunSeederAsync(CalendarSeederName, userId);

        var deletedDate = new DateOnly(DateTime.UtcNow.Year, 6, 15);
        int countBefore;
        await using (var db = CreateDbContext())
        {
            var day = await db.Set<Calendar>().IgnoreQueryFilters()
                .SingleAsync(c => c.UserId == userId && c.Date == deletedDate, CancellationToken);
            countBefore = (await CalendarIdsOf(db, userId)).Count;
            db.Remove(day);
            await db.SaveChangesAsync(CancellationToken);
        }

        var refill = () => RunSeederAsync(CalendarSeederName, userId);
        await refill.Should().NotThrowAsync();

        await using var afterDb = CreateDbContext();
        (await CalendarIdsOf(afterDb, userId)).Should().HaveCount(countBefore);
        (await afterDb.Set<Calendar>().IgnoreQueryFilters()
            .CountAsync(c => c.UserId == userId && c.Date == deletedDate, CancellationToken))
            .Should().Be(1);
    }

    /// <summary>
    /// <c>CalendarSeeder.ResetDefaults</c> deliberately declines — a day carries the user's notes,
    /// labels and sleep window, and there is nothing to reset it <i>to</i>. The manager falls through to
    /// <c>SetupDefaults</c>, so what has to be true is that an override run is a no-op over existing
    /// days rather than a rewrite of them.
    /// </summary>
    [Fact]
    public async Task Calendar_Reset_DeclinesAndDestroysNothing()
    {
        var userId = await CreateUserAsync();
        await RunSeederAsync(CalendarSeederName, userId);

        var editedDate = new DateOnly(DateTime.UtcNow.Year, 3, 9);
        long editedId;
        int countBefore;
        await using (var db = CreateDbContext())
        {
            var day = await db.Set<Calendar>().IgnoreQueryFilters()
                .SingleAsync(c => c.UserId == userId && c.Date == editedDate, CancellationToken);
            editedId = day.Id;
            day.WakeUpTime = new TimeOnly(5, 30);
            day.Notes = "mine";
            await db.SaveChangesAsync(CancellationToken);
            countBefore = (await CalendarIdsOf(db, userId)).Count;
        }

        var reset = () => RunSeederAsync(CalendarSeederName, userId, overrideData: true);
        await reset.Should().NotThrowAsync("declining to reset is a fallback, not a failure");

        await using var afterDb = CreateDbContext();
        (await CalendarIdsOf(afterDb, userId)).Should().HaveCount(countBefore);
        var reloaded = await afterDb.Set<Calendar>().IgnoreQueryFilters()
            .SingleAsync(c => c.Id == editedId, CancellationToken);
        reloaded.WakeUpTime.Should().Be(new TimeOnly(5, 30));
        reloaded.Notes.Should().Be("mine");
    }

    private static int DaysIn(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    private async Task<List<DateOnly>> CalendarDatesOf(DbContext db, long userId) =>
        await db.Set<Calendar>().IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Date)
            .Select(c => c.Date)
            .ToListAsync(CancellationToken);

    private async Task<List<long>> CalendarIdsOf(DbContext db, long userId) =>
        await db.Set<Calendar>().IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(CancellationToken);

    // ---- helpers -------------------------------------------------------------------------------------

    private IServiceScope NewScope() => Fixture.UnauthenticatedFactory.Services.CreateScope();

    private async Task RunSeederAsync(string seederName, long userId, bool overrideData = false)
    {
        using var scope = NewScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPerUserDefaultSeederManager>();
        await manager.SeedForUserAsync(seederName, userId, overrideData, CancellationToken);
    }

    private async Task<long> CreateUserAsync()
    {
        var email = $"seeder-{Guid.NewGuid():N}@test.com";
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

        // Deliberately not the registration endpoint: that runs UserDefaultsService and would leave the
        // user already seeded, which is the state these tests are trying to arrive at themselves.
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    /// <summary>Changes one string property on the row currently holding <paramref name="original"/>, returning its id.</summary>
    private async Task<long> RenameAsync(IEntityProbe probe, long userId, string property, string original, string renamed)
    {
        await using var db = CreateDbContext();
        var rows = await probe.RowsForUserAsync(db, userId, CancellationToken);
        var row = rows.Single(r => Equals(Get(r, property), original));

        Set(row, property, renamed);
        await db.SaveChangesAsync(CancellationToken);
        return IdOf(row);
    }

    private static long IdOf(object row) => (long)Get(row, nameof(IEntityWithUser.Id))!;

    private static object? Get(object row, string property) =>
        row.GetType().GetProperty(property)!.GetValue(row);

    private static void Set(object row, string property, object? value) =>
        row.GetType().GetProperty(property)!.SetValue(row, value);

    /// <summary>
    /// Reading rows for a seeder named only by its entity <see cref="Type"/>. <c>DbContext.Set&lt;T&gt;</c>
    /// is generic and <c>IEntityWithUser</c> is what the seeders are constrained on, so the type argument
    /// is supplied once here rather than by every caller.
    /// </summary>
    private interface IEntityProbe
    {
        Task<List<object>> RowsForUserAsync(DbContext db, long userId, CancellationToken ct);
        Task<List<object>> AllRowsAsync(DbContext db, CancellationToken ct);
    }

    private sealed class EntityProbe<TEntity> : IEntityProbe where TEntity : class, IEntityWithUser
    {
        public async Task<List<object>> RowsForUserAsync(DbContext db, long userId, CancellationToken ct) =>
            (await Unfiltered(db)
                .Where(e => EF.Property<long>(e, nameof(IEntityWithUser.UserId)) == userId)
                .ToListAsync(ct))
            .Cast<object>()
            .ToList();

        public async Task<List<object>> AllRowsAsync(DbContext db, CancellationToken ct) =>
            (await Unfiltered(db).ToListAsync(ct)).Cast<object>().ToList();

        // Fixture contexts carry no user filter anyway; IgnoreQueryFilters keeps that an explicit
        // property of the assertion rather than something inherited from how the context was built.
        private static IQueryable<TEntity> Unfiltered(DbContext db) =>
            db.Set<TEntity>().IgnoreQueryFilters().OrderBy(e => EF.Property<long>(e, nameof(IEntityWithUser.Id)));
    }

    private static IEntityProbe ProbeFor(Type entityType) =>
        (IEntityProbe)Activator.CreateInstance(typeof(EntityProbe<>).MakeGenericType(entityType))!;
}
