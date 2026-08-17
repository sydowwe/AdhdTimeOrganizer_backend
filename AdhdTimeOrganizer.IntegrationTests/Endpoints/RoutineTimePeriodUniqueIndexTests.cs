using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.infrastructure.persistence.seeder.userDefault;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section D — <c>RoutineTimePeriod</c> carries two per-user unique indexes,
/// <c>(UserId, Text)</c> and <c>(UserId, LengthInDays)</c>, declared in
/// <c>RoutineTimePeriodConfiguration.cs:13</c> and <c>:79</c>. Either one alone is enough to reject a
/// row, which is easy to forget when only the display-text index is top of mind.
/// </summary>
[Collection("Postgres")]
public class RoutineTimePeriodUniqueIndexTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Creating a second period with the same Text 409s")]
    public async Task Create_DuplicateText_Returns409()
    {
        await using (var db = CreateDbContext())
            await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "Daily-ish", lengthInDays: 3);

        var response = await CreateClient().PostAsJsonAsync("api/routine-time-period", new
        {
            Text = "Daily-ish",
            Color = "#abcdef",
            LengthInDays = 9, // different length, same text
            StreakThreshold = 90,
            StreakGraceDays = 0,
            ResetAnchorDay = 1, // 0 ("rolling") is DB-check-illegal for LengthInDays > 7 -- see the dedicated finding test below
            HistoryDepth = 16
        }, JsonOpts, CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
    }

    [Fact(DisplayName = "Creating a second period with the same LengthInDays but a different Text ALSO 409s (the one people forget)")]
    public async Task Create_DuplicateLengthInDays_DifferentText_Returns409()
    {
        await using (var db = CreateDbContext())
            await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "Fortnightly", lengthInDays: 14);

        var response = await CreateClient().PostAsJsonAsync("api/routine-time-period", new
        {
            Text = "Biweekly", // different text
            Color = "#abcdef",
            LengthInDays = 14, // same length
            StreakThreshold = 90,
            StreakGraceDays = 0,
            ResetAnchorDay = 1,
            HistoryDepth = 16
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "(UserId, LengthInDays) is a second, independent unique index — colliding on it alone must also 409");
    }

    [Fact(DisplayName = "The same Text and LengthInDays for a different user succeeds — the indexes are per-user")]
    public async Task Create_SameValues_DifferentUser_Succeeds()
    {
        long otherUserId = ReminderSeedHelper.OtherUserId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, otherUserId, text: "Shared-Name", lengthInDays: 21);
        }

        var response = await CreateClient().PostAsJsonAsync("api/routine-time-period", new
        {
            Text = "Shared-Name",
            Color = "#abcdef",
            LengthInDays = 21,
            StreakThreshold = 90,
            StreakGraceDays = 0,
            ResetAnchorDay = 1,
            HistoryDepth = 16
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the unique indexes are scoped per-user, not global");
    }

    /// <summary>
    /// DOC-6/MIG-8: <c>RoutineTimePeriodSeeder.Collides</c> (<c>RoutineTimePeriodSeeder.cs:29</c>) reads
    /// <c>a.Text == b.Text || a.LengthInDays == b.LengthInDays</c> — verified here to cover <b>both</b>
    /// indexes. Had it only compared <c>Text</c>, sign-up seeding of a brand-new user would still throw
    /// 23505 the moment two defaults happened to share a length (none of the current four do, but the
    /// gap would still be live). This exercises the real seeder end-to-end against Postgres rather than
    /// re-deriving the matcher logic, unlike <c>PerUserDefaultMatcherTests</c> which is a fixture-only
    /// unit test of the shared matcher.
    /// </summary>
    [Fact(DisplayName = "RoutineTimePeriodSeeder.SetupDefaults seeds all four defaults for a brand-new user without a 23505")]
    public async Task Seeder_SetupDefaults_SeedsAllFourDefaultsWithoutUniqueViolation()
    {
        long newUserId = ReminderSeedHelper.OtherUserId;
        await using var db = CreateDbContext();
        await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);

        var seeder = new RoutineTimePeriodSeeder(db, NullLogger<RoutineTimePeriodSeeder>.Instance);

        var act = async () => await seeder.SetupDefaults(newUserId, CancellationToken);

        await act.Should().NotThrowAsync("Collides must gate on both unique indexes, or seeding a fresh user throws 23505");

        var seeded = await db.Set<RoutineTimePeriod>()
            .IgnoreQueryFilters()
            .Where(p => p.UserId == newUserId)
            .ToListAsync(CancellationToken);

        seeded.Should().HaveCount(4);
        seeded.Select(p => p.Text).Should().BeEquivalentTo(["Daily", "Weekly", "Monthly", "Yearly"]);
        seeded.Select(p => p.LengthInDays).Should().OnlyHaveUniqueItems("no two defaults may share a length either");
    }

    /// <summary>
    /// <c>RoutineTimePeriodRequest.ResetAnchorDay</c>'s doc comment and <c>RoutineTimePeriodValidator</c>
    /// both say <c>0</c> means "rolling" and accept it unconditionally (<c>InclusiveBetween(0, 7)</c> /
    /// <c>InclusiveBetween(0, 30)</c>). <c>ck_routine_time_period_reset_anchor_day_range</c>
    /// (<c>RoutineTimePeriodConfiguration.cs</c>) now allows <c>reset_anchor_day BETWEEN 0 AND 7</c>
    /// (weekly-aligned) or <c>BETWEEN 0 AND 30</c> (non-weekly), matching the validator, and the AND/OR
    /// grouping is parenthesized so a short period (<c>length_in_days &lt;= 7</c>) no longer makes the
    /// whole constraint a no-op. This test pins both halves: <c>ResetAnchorDay = 0</c> succeeds for a
    /// 7-day (weekly-aligned) period and for an 8-day (non-weekly) period alike.
    /// </summary>
    [Fact(DisplayName = "ResetAnchorDay == 0 (\"rolling\") is accepted both at and beyond LengthInDays == 7")]
    public async Task ResetAnchorDayZero_RollingSentinel_SucceedsAtAndBeyondOneWeek()
    {
        var client = CreateClient();

        var shortPeriod = await client.PostAsJsonAsync("api/routine-time-period", new
        {
            Text = "rolling-short",
            Color = "#abcdef",
            LengthInDays = 7,
            StreakThreshold = 90,
            StreakGraceDays = 0,
            ResetAnchorDay = 0,
            HistoryDepth = 16
        }, JsonOpts, CancellationToken);
        shortPeriod.StatusCode.Should().Be(HttpStatusCode.Created, "0 is within the weekly-aligned BETWEEN 0 AND 7 range");

        var longPeriodBody = new
        {
            Text = "rolling-long",
            Color = "#abcdef",
            LengthInDays = 8,
            StreakThreshold = 90,
            StreakGraceDays = 0,
            ResetAnchorDay = 0,
            HistoryDepth = 16
        };
        var longPeriod = await client.PostAsJsonAsync("api/routine-time-period", longPeriodBody, JsonOpts, CancellationToken);
        var longPeriodResponseBody = await longPeriod.Content.ReadAsStringAsync(CancellationToken);

        longPeriod.StatusCode.Should().Be(HttpStatusCode.Created, longPeriodResponseBody);
    }

    /// <summary>
    /// Regression pin for the operator-precedence bug fixed in <c>ck_routine_time_period_reset_anchor_day_range</c>:
    /// before the fix, <c>AND</c> binding tighter than <c>OR</c> made the whole constraint a no-op for any
    /// <c>length_in_days &lt;= 7</c>, so an out-of-range anchor slipped through whenever the API validator
    /// was bypassed (e.g. direct EF writes, or any future caller that skips validation). This writes directly
    /// via EF to bypass <c>RoutineTimePeriodValidator</c> and asserts the DB layer itself now rejects it.
    /// </summary>
    [Fact(DisplayName = "DB check constraint rejects an out-of-range ResetAnchorDay on a short period, not just a no-op")]
    public async Task ResetAnchorDay_OutOfRange_OnShortPeriod_RejectedAtDbLayer()
    {
        await using var db = CreateDbContext();
        var entity = new RoutineTimePeriod
        {
            UserId = TodoListTestSeedHelper.TestUserId,
            Text = "short-out-of-range-anchor",
            Color = "#abcdef",
            LengthInDays = 7,
            StreakThreshold = 90,
            StreakGraceDays = 0,
            ResetAnchorDay = 8, // out of the weekly-aligned BETWEEN 0 AND 7 range
            HistoryDepth = 16
        };
        db.Set<RoutineTimePeriod>().Add(entity);

        var act = async () => await db.SaveChangesAsync(CancellationToken);

        await act.Should().ThrowAsync<Exception>(
            "the fixed CHECK constraint parenthesizes AND/OR so it is no longer a no-op for LengthInDays <= 7");
    }
}
