using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Planning.infrastructure.persistence.seeder.userDefault;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.infrastructure.persistence.seeder.@interface;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Seeding;

/// <summary>
/// TEST-22 — <c>CalendarSeeder</c>, the one per-user default seeder that is <b>not</b> a
/// <c>BasePerUserDefaultSeeder</c>.
/// <para>
/// Its key is a date range rather than a row set, so it hand-rolls <c>SetupDefaults</c> /
/// <c>ResetDefaults</c> and does its own <c>IgnoreQueryFilters</c> — deliberately, per the slice's
/// <c>docs/summary.md</c>. That is exactly why the shared guarantees <c>PerUserDefaultSeederTests</c> and
/// <c>PerUserDefaultMatcherTests</c> give every other seeder do not reach it, and why the three
/// load-bearing behaviours below need pinning here.
/// </para>
/// <para>
/// <c>PerUserDefaultSeederTests</c> already covers the coarse shape (a first run fills two years, a second
/// adds nothing, one deleted day comes back, an override run destroys nothing). This file covers what that
/// shape leaves open: a year gapped in several places, the window's <i>edges</i>, the <c>false</c> that
/// <c>ResetDefaults</c> returns to its caller, the ambient-user case that breaks if the
/// <c>IgnoreQueryFilters</c> goes, and the country code the seeder chooses.
/// </para>
/// <para>
/// Every run gets a fresh DI scope, as production does — a single scope would leave the previous run's rows
/// tracked by the same <c>DbContext</c> and mask an insert the database would have rejected.
/// </para>
/// </summary>
[Collection("Postgres")]
public class CalendarSeederTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const string Password = "Test@1234!";

    private static int ThisYear => DateTime.UtcNow.Year;

    // ---- Scenario A — fills per missing date, not per year -------------------------------------------

    /// <summary>
    /// The comment on <c>SeedYearForUser</c> spells out the rule: <i>"the unique index is (user_id, date),
    /// so a year the user has partially deleted has to be filled in rather than skipped whole or re-inserted
    /// whole."</i> Several scattered gaps rather than one, and an assertion that the year holds exactly as
    /// many rows as it has days, so both failure modes of a "does this year have any rows at all" check are
    /// caught: skipping leaves the gaps open, re-inserting collides on the index.
    /// </summary>
    [Fact(DisplayName = "Re-seeding a year the user has partly deleted fills the gaps and nothing else")]
    public async Task Setup_OverAPartiallyDeletedYear_RefillsEveryMissingDate_WithoutColliding()
    {
        var userId = await CreateUserAsync();
        await SetupDefaultsAsync(userId);

        // Days with no planner tasks — planner_task.calendar_id is Restrict, so a day carrying one
        // cannot be deleted at all and the test would be arranging a state it never reaches.
        DateOnly[] deleted =
        [
            new(ThisYear, 3, 5),
            new(ThisYear, 7, 17),
            new(ThisYear, 12, 31)
        ];

        await using (var db = CreateDbContext())
        {
            var days = await db.Set<Calendar>().IgnoreQueryFilters()
                .Where(c => c.UserId == userId && deleted.Contains(c.Date))
                .ToListAsync(CancellationToken);
            days.Should().HaveCount(deleted.Length, "the arrangement only means anything if those days existed");
            db.RemoveRange(days);
            await db.SaveChangesAsync(CancellationToken);
        }

        var refill = () => SetupDefaultsAsync(userId);
        await refill.Should().NotThrowAsync("re-seeding a gapped year is the replay path, not a 23505");

        await using var afterDb = CreateDbContext();
        var dates = await DatesOfAsync(afterDb, userId, ThisYear);

        dates.Should().HaveCount(DaysIn(ThisYear),
            "skipping the year whole leaves the gaps open and re-inserting it whole would have thrown — only a per-date fill lands here");
        dates.Should().OnlyHaveUniqueItems("(user_id, date) is unique");
        dates.Should().Contain(deleted);
    }

    // ---- Scenario B — the rolling year window --------------------------------------------------------

    /// <summary>
    /// <c>SeedYears()</c> returns <c>[thisYear, thisYear + 1]</c>, resolved when the seeder runs. It used to
    /// be a hard-coded <c>{ 2025, 2026 }</c>, which set an expiry date on the planner: past the last seeded
    /// year every date resolved to no calendar row, and with no create-calendar endpoint nothing in the app
    /// could make one. Lazy creation (<c>CalendarProvisioner</c>) is the actual fix — see
    /// <c>LazyCalendarCreationTests</c>; the rolling window only helps users created from now on, so this is
    /// hygiene.
    /// <para>
    /// Every expectation is derived from <c>DateTime.UtcNow.Year</c>. A literal would keep passing for the
    /// wrong reason the moment the calendar turns over.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "Setup seeds this year and next, and no year on either side of them")]
    public async Task Setup_SeedsExactlyThisYearAndNext()
    {
        var userId = await CreateUserAsync();

        await SetupDefaultsAsync(userId);

        await using var db = CreateDbContext();

        (await DatesOfAsync(db, userId, ThisYear)).Should().HaveCount(DaysIn(ThisYear));
        (await DatesOfAsync(db, userId, ThisYear + 1)).Should().HaveCount(DaysIn(ThisYear + 1));

        (await DatesOfAsync(db, userId, ThisYear - 1)).Should().BeEmpty(
            "the window rolls forward; the seeder never backfills");
        (await DatesOfAsync(db, userId, ThisYear + 2)).Should().BeEmpty(
            "a third year would mean the window stopped being two years wide");
    }

    // ---- Scenario C — ResetDefaults is a deliberate no-op ---------------------------------------------

    /// <summary>
    /// Every other per-user default seeder resets to its defaults. This one cannot: "the default" is a year
    /// of dates, and a day carries the user's own day type, label, sleep window and notes — there is nothing
    /// to reset it <i>to</i>. So it logs and returns <c>false</c>.
    /// <para>
    /// Both halves matter. The <c>false</c> is the only signal the caller gets (the manager reads it and
    /// falls through to <c>SetupDefaults</c>), and a future refactor that "helpfully" makes reset re-seed
    /// would wipe every user's day metadata across two years.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "ResetDefaults declines and leaves the user's edited day exactly as it was")]
    public async Task Reset_ReturnsFalse_AndRewritesNothing()
    {
        var userId = await CreateUserAsync();
        await SetupDefaultsAsync(userId);

        var editedDate = new DateOnly(ThisYear, 2, 10);
        long editedId;
        await using (var db = CreateDbContext())
        {
            var day = await db.Set<Calendar>().IgnoreQueryFilters()
                .SingleAsync(c => c.UserId == userId && c.Date == editedDate, CancellationToken);
            editedId = day.Id;
            day.DayType = DayType.Vacation;
            day.Label = "HomeOffice";
            day.WakeUpTime = new TimeOnly(5, 45);
            await db.SaveChangesAsync(CancellationToken);
        }

        bool reset;
        using (var scope = NewScope())
            reset = await SeederIn(scope).ResetDefaults(userId, CancellationToken);

        reset.Should().BeFalse(
            "the return value is the whole contract — the manager falls through to SetupDefaults on false");

        await using var afterDb = CreateDbContext();
        var reloaded = await afterDb.Set<Calendar>().IgnoreQueryFilters()
            .SingleAsync(c => c.Id == editedId, CancellationToken);

        reloaded.DayType.Should().Be(DayType.Vacation);
        reloaded.Label.Should().Be("HomeOffice");
        reloaded.WakeUpTime.Should().Be(new TimeOnly(5, 45));
    }

    // ---- Scenario D — hand-scoping --------------------------------------------------------------------

    /// <summary>
    /// <c>SeedYearForUser</c> reads the existing dates with <c>IgnoreQueryFilters()</c> and an explicit
    /// <c>c.UserId == userId</c>, for the same reason <c>CalendarProvisioner.FindAsync</c> does: the seeder is
    /// told which user to seed, which is not necessarily the one signed in, and the host scopes
    /// <c>IEntityWithUser</c> reads to the ambient user. Filtered, it would read back no dates for B and
    /// re-insert B's whole year onto <c>(user_id, date)</c>.
    /// <para>
    /// So this runs the seeder from a host whose ambient logged user is <b>A</b> while asking it to seed
    /// <b>B</b>, with B's year already partly filled. Remove the <c>IgnoreQueryFilters()</c> and it breaks as
    /// a unique-index violation during <i>user registration</i>, which is a bad place to find out.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "Seeding B's gapped year while signed in as A fills only B's gaps")]
    public async Task Setup_ForAnotherUser_ReadsThatUsersDates_NotTheAmbientUsers()
    {
        var userIdA = await CreateUserAsync();
        var userIdB = await CreateUserAsync();

        await SetupDefaultsAsync(userIdA);
        await SetupDefaultsAsync(userIdB);

        DateOnly[] deleted = [new(ThisYear, 4, 2), new(ThisYear, 9, 19)];
        List<long> idsOfA;
        await using (var db = CreateDbContext())
        {
            var days = await db.Set<Calendar>().IgnoreQueryFilters()
                .Where(c => c.UserId == userIdB && deleted.Contains(c.Date))
                .ToListAsync(CancellationToken);
            days.Should().HaveCount(deleted.Length);
            db.RemoveRange(days);
            await db.SaveChangesAsync(CancellationToken);

            idsOfA = await IdsOfAsync(db, userIdA);
        }

        // A real host signed in as A: its DbContext's ScopeUserId is A, so every unqualified read of a
        // user-scoped entity inside the seeder sees A's rows and only A's.
        await using var asUserA = CreateFactory(TestRoles.User, userIdA);
        using (var scope = asUserA.Services.CreateScope())
        {
            var seedB = () => SeederIn(scope).SetupDefaults(userIdB, CancellationToken);
            await seedB.Should().NotThrowAsync(
                "the seeder reads B's dates unfiltered; reading A's would make B's surviving days look missing");
        }

        await using var afterDb = CreateDbContext();

        var datesOfB = await DatesOfAsync(afterDb, userIdB, ThisYear);
        datesOfB.Should().HaveCount(DaysIn(ThisYear));
        datesOfB.Should().Contain(deleted);

        (await IdsOfAsync(afterDb, userIdA)).Should().Equal(idsOfA,
            "A's days are not the seeder's to touch when it was told to seed B");
    }

    // ---- Scenario E — the country code the seeder chooses ---------------------------------------------

    /// <summary>
    /// <c>CalendarSeeder</c> and <c>CalendarProvisioner</c> both build rows through
    /// <c>CalendarDayFactory.Create</c>, and
    /// <c>LazyCalendarCreationTests.ALazilyCreatedDay_CarriesTheSameMetadataTheSeederWouldHaveGivenIt</c>
    /// already pins that the two agree on a day's metadata — not repeated here.
    /// <para>
    /// What is unpinned is the <i>country code</i>. <c>SetupDefaults</c> passes
    /// <c>HolidayCalendar.DefaultCountryCode</c>, but the public <c>SeedYear(year, countryCode, userId)</c>
    /// overload lets a caller pass something else and nothing checks what happens then. 1 September is a
    /// Slovak holiday and not a Czech one; 28 September is the reverse, which makes the pair a code that
    /// cannot be satisfied by both.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "Setup uses the default country code; SeedYear honours the one it is handed")]
    public async Task Setup_UsesTheDefaultCountryCode_AndSeedYearHonoursAnExplicitOne()
    {
        HolidayCalendar.DefaultCountryCode.Should().Be("SK", "the assertions below are written against it");

        var userId = await CreateUserAsync();
        await SetupDefaultsAsync(userId);

        await using (var db = CreateDbContext())
        {
            (await HolidayNameAsync(db, userId, new DateOnly(ThisYear, 9, 1)))
                .Should().Be("Deň Ústavy Slovenskej republiky", "SetupDefaults seeds the default country");
            (await HolidayNameAsync(db, userId, new DateOnly(ThisYear, 9, 28)))
                .Should().BeNull("Czech statehood day is not a Slovak holiday");
        }

        // A year well outside the rolling window, so this cannot collide with what SetupDefaults wrote.
        var otherYear = ThisYear + 5;
        using (var scope = NewScope())
            await SeederIn(scope).SeedYear(otherYear, "CZ", userId);

        await using var afterDb = CreateDbContext();
        (await DatesOfAsync(afterDb, userId, otherYear)).Should().HaveCount(DaysIn(otherYear));
        (await HolidayNameAsync(afterDb, userId, new DateOnly(otherYear, 9, 28)))
            .Should().Be("Den české státnosti", "the overload seeds the country code it was handed");
        (await HolidayNameAsync(afterDb, userId, new DateOnly(otherYear, 9, 1)))
            .Should().BeNull("Slovak constitution day is not a Czech holiday");
    }

    // ---- helpers --------------------------------------------------------------------------------------

    private static int DaysIn(int year) => DateTime.IsLeapYear(year) ? 366 : 365;

    private IServiceScope NewScope() => Fixture.UnauthenticatedFactory.Services.CreateScope();

    /// <summary>
    /// The concrete seeder. The DI scan registers <c>IScopedService</c> implementations
    /// <c>AsImplementedInterfaces</c> only, so the concrete type is not resolvable directly — and asking for
    /// it by name off <c>IPerUserDefaultSeeder</c> is also what proves it is still registered as one.
    /// </summary>
    private static CalendarSeeder SeederIn(IServiceScope scope) =>
        scope.ServiceProvider.GetServices<IPerUserDefaultSeeder>().OfType<CalendarSeeder>().Single();

    private async Task SetupDefaultsAsync(long userId)
    {
        using var scope = NewScope();
        await SeederIn(scope).SetupDefaults(userId, CancellationToken);
    }

    private async Task<List<DateOnly>> DatesOfAsync(DbContext db, long userId, int year) =>
        await db.Set<Calendar>().IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Date.Year == year)
            .OrderBy(c => c.Date)
            .Select(c => c.Date)
            .ToListAsync(CancellationToken);

    private async Task<List<long>> IdsOfAsync(DbContext db, long userId) =>
        await db.Set<Calendar>().IgnoreQueryFilters()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(CancellationToken);

    private async Task<string?> HolidayNameAsync(DbContext db, long userId, DateOnly date) =>
        await db.Set<Calendar>().IgnoreQueryFilters()
            .Where(c => c.UserId == userId && c.Date == date)
            .Select(c => c.HolidayName)
            .SingleAsync(CancellationToken);

    /// <summary>
    /// Deliberately not the registration endpoint: that runs <c>UserDefaultsService</c> and would leave the
    /// user already seeded, which is the state these tests arrive at themselves.
    /// </summary>
    private async Task<long> CreateUserAsync()
    {
        var email = $"calendar-seeder-{Guid.NewGuid():N}@test.com";
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

        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        return user.Id;
    }
}
