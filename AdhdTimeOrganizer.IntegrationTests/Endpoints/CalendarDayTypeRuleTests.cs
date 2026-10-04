using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>CalendarDayTypeRule</c> — one implementation behind two callers with deliberately different failure
/// shapes: <c>UpdateCalendarEndpoint</c> turns a violation into a <b>400</b> from the day-edit dialog, while
/// <c>BatchUpdateDayTypeCalendarEndpoint</c> turns the same violation into that one day's
/// <c>succeeded: false</c> so a selected week is not cancelled by the Saturday in it.
/// <para>
/// Every scenario is therefore run through <b>both</b> routes in one test: the two must reach the same
/// <i>decision</i> for the same day and day type and differ only in how they report it. That is what keeps the
/// single shared implementation honest — a rewrite that only satisfies one caller has to fail here.
/// </para>
/// <para>
/// For every rejected case the row is re-read and asserted <i>wholly</i> unchanged, not just its day type.
/// <c>UpdateCalendarEndpoint</c> validates in <c>AfterMapping</c> — after <c>UpdateEntity</c> has already
/// written every field onto the tracked entity — and relies on <c>ThrowError</c> aborting before
/// <c>SaveChanges</c>. A refactor that moved the save earlier would still answer 400 with the write committed.
/// </para>
/// <para>
/// Dates are in 2029, outside the window <c>CalendarSeeder</c> fills (this year and next), so each day is the
/// one this test seeded rather than a seeded row with a day type of its own.
/// </para>
/// </summary>
[Collection("Postgres")]
public class CalendarDayTypeRuleTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // Two dates per weekday: one drives the PUT, the other the batch PATCH, so both routes decide the same
    // scenario in the same test without sharing a row.
    private static readonly Dictionary<DayOfWeek, (DateOnly ForPut, DateOnly ForBatch)> Dates = new()
    {
        [DayOfWeek.Saturday] = (new DateOnly(2029, 3, 3), new DateOnly(2029, 3, 10)),
        [DayOfWeek.Sunday] = (new DateOnly(2029, 3, 4), new DateOnly(2029, 3, 11)),
        [DayOfWeek.Wednesday] = (new DateOnly(2029, 3, 7), new DateOnly(2029, 3, 14))
    };

    /// <summary>
    /// The whole rule, as a table. Only <c>Weekend</c> and <c>Workday</c> are constrained; the other three day
    /// types take the <c>_ =&gt; null</c> arm and are legal on any weekday.
    /// </summary>
    public static TheoryData<DayOfWeek, DayType, bool> Scenarios => new()
    {
        // Sunday is the row that matters most: Calendar.DayIndex maps Sunday to 7 (not DayOfWeek's 0), and a
        // rewrite of IsWeekend in terms of DayOfWeek without that mapping breaks Sunday first.
        { DayOfWeek.Saturday, DayType.Workday, false },
        { DayOfWeek.Sunday, DayType.Workday, false },
        { DayOfWeek.Wednesday, DayType.Weekend, false },
        { DayOfWeek.Saturday, DayType.Weekend, true },
        { DayOfWeek.Wednesday, DayType.Workday, true },
        { DayOfWeek.Saturday, DayType.Vacation, true },
        { DayOfWeek.Wednesday, DayType.SickDay, true },
        { DayOfWeek.Wednesday, DayType.Special, true }
    };

    [Theory(DisplayName = "The weekday rule reaches the same decision on the PUT and on the batch PATCH")]
    [MemberData(nameof(Scenarios))]
    public async Task DayTypeRule_DecidesTheSameOnBothRoutes(DayOfWeek weekday, DayType dayType, bool allowed)
    {
        var (putDate, batchDate) = Dates[weekday];
        var initialDayType = InitialDayTypeFor(dayType);

        long putDay, batchDay;
        await using (var db = CreateDbContext())
        {
            putDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, putDate, UserId, CancellationToken, initialDayType);
            batchDay = await PlanningTestSeedHelper.SeedCalendarAsync(db, batchDate, UserId, CancellationToken, initialDayType);
        }

        // ─── the 400 half: PUT /calendar/{id}, the day-edit dialog ───────────
        var putResponse = await CreateUserRoleClient()
            .PutAsJsonAsync($"api/calendar/{putDay}", UpdatePayload(dayType), JsonOpts, CancellationToken);
        var putBody = await putResponse.Content.ReadAsStringAsync(CancellationToken);

        putResponse.StatusCode.Should().Be(allowed ? HttpStatusCode.OK : HttpStatusCode.BadRequest, putBody);

        if (!allowed)
        {
            putBody.Should().Contain(weekday.ToString(),
                "the message names the weekday the day actually falls on, so the dialog can explain the refusal");
            // The date is interpolated with DateOnly.ToString(), whose separators follow the host's culture —
            // the year is the culture-independent part of it.
            putBody.Should().Contain(putDate.Year.ToString(), "the message names the offending date");
        }

        // ─── the succeeded:false half: PATCH /calendar/day-type/batch ────────
        var batchResponse = await CreateUserRoleClient().PatchAsJsonAsync("api/calendar/day-type/batch", new
        {
            CalendarIds = new[] { batchDay },
            DayType = dayType.ToString()
        }, JsonOpts, CancellationToken);

        batchResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the batch reports a rule violation per day, never as an HTTP failure");

        var batchBody = await batchResponse.Content.ReadFromJsonAsync<CalendarBatchResultResponse>(JsonOpts, CancellationToken);
        var expected = allowed
            ? CalendarBatchResult.Ok(batchDay)
            : CalendarBatchResult.Failed(batchDay, CalendarBatchFailureReason.DayTypeNotAllowed);
        batchBody!.Results.Should().Equal([expected],
            "both callers share one rule and must agree on the decision, differing only in how they report it");

        // ─── the rows ────────────────────────────────────────────────────────
        await using (var verifyDb = CreateDbContext())
        {
            var put = await verifyDb.Set<Calendar>().SingleAsync(c => c.Id == putDay, CancellationToken);
            var batch = await verifyDb.Set<Calendar>().SingleAsync(c => c.Id == batchDay, CancellationToken);

            if (allowed)
            {
                put.DayType.Should().Be(dayType);
                batch.DayType.Should().Be(dayType);

                // The cheapest place to give Label / Location / Weather / Notes any behavioural coverage at all.
                put.Label.Should().Be("Focus day");
                put.Location.Should().Be(Location.Office);
                put.Weather.Should().Be("Rain");
                put.Notes.Should().Be("Ship the thing");
                put.WakeUpTime.Should().Be(new TimeOnly(8, 0));
                put.BedTime.Should().Be(new TimeOnly(0, 0), "midnight is a legal bed time, not an empty one");
            }
            else
            {
                put.DayType.Should().Be(initialDayType, "a 400 leaves the day exactly as it was");

                // AfterMapping runs after UpdateEntity has already written all seven fields onto the tracked
                // entity; these assert ThrowError really did abort before SaveChanges.
                put.Label.Should().BeNull();
                put.Location.Should().BeNull();
                put.Weather.Should().BeNull();
                put.Notes.Should().BeNull();
                put.WakeUpTime.Should().Be(new TimeOnly(7, 0), "the whole update is discarded, not only the day type");
                put.BedTime.Should().Be(new TimeOnly(23, 0));

                batch.DayType.Should().Be(initialDayType);
            }
        }
    }

    /// <summary>
    /// The rule is about the day the row falls on, not the day the caller thinks it is: <c>CalendarRequest</c>
    /// carries no date, and <c>Calendar.Date</c> is <c>init</c>-only, so a successful update cannot move a day
    /// off the weekday its type was checked against.
    /// </summary>
    [Fact(DisplayName = "A successful update leaves the day's date alone — the rule's only input")]
    public async Task Update_DoesNotMoveTheDate()
    {
        var date = new DateOnly(2029, 3, 21); // a Wednesday
        long calendarId;
        await using (var db = CreateDbContext())
            calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken, DayType.Vacation);

        var response = await CreateUserRoleClient()
            .PutAsJsonAsync($"api/calendar/{calendarId}", UpdatePayload(DayType.Workday), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));

        await using (var verifyDb = CreateDbContext())
        {
            var calendar = await verifyDb.Set<Calendar>().SingleAsync(c => c.Id == calendarId, CancellationToken);
            calendar.Date.Should().Be(date);
            calendar.DayType.Should().Be(DayType.Workday);
        }
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Any day type other than the one under test, so "unchanged" is an observable outcome.</summary>
    private static DayType InitialDayTypeFor(DayType dayType) =>
        dayType == DayType.Vacation ? DayType.Special : DayType.Vacation;

    /// <summary>
    /// A full <c>CalendarRequest</c> — the PUT is a whole-row update, so every field travels on every request,
    /// which is exactly why a 400 arriving after the mapping is worth pinning.
    /// </summary>
    private static object UpdatePayload(DayType dayType) => new
    {
        DayType = dayType.ToString(),
        Label = "Focus day",
        WakeUpTime = new { Hours = 8, Minutes = 0 },
        BedTime = new { Hours = 0, Minutes = 0 },
        Location = "Office",
        Weather = "Rain",
        Notes = "Ship the thing"
    };
}
