using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.application.dto.dto;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.application.validator;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// `TEST-3` — <c>POST api/activity-history/dashboard/calendar</c>: the day fold, the range's edges,
/// <c>topN</c>, and cross-user isolation. Before this suite the endpoint had exactly one test
/// (<see cref="HistoryDashboardGroupIdentityTests.Calendar_TopRolesCarryTheRoleId"/>) and it was about
/// <c>roleId</c>.
///
/// <para>This is the one dashboard that reads another slice's entity — the days come from Core's
/// <c>ICalendarDayLookup</c>, never from Planning's <c>Calendar</c> — and the response is driven by
/// <b>those days</b>, not by the history rows. That is what makes the fold load-bearing: the days are
/// keyed by a bare <c>DateOnly</c>, which is the <i>user's</i> calendar day, so history folded in UTC
/// lines up against a user-day calendar and lands a row one cell early or late. `DOC-2`: the endpoint
/// did exactly that until this change, and never resolved <c>IUserTimeZoneResolver</c> at all, alone
/// among the dashboards.</para>
///
/// <para>Nothing here asserts a status code where a value is available. Every way this endpoint has been
/// wrong produced a well-formed 200 holding a plausible number: a row on the wrong day still totals
/// correctly across the range, an unbounded range still answers, and a day cell short of one session
/// looks like a day the user did not log. The 400s that <i>are</i> asserted
/// (<c>CalendarActivityRequestValidator</c>, `SEC-3`) are the cases where refusing to answer is the
/// contract.</para>
/// </summary>
[Collection("Postgres")]
public class HistoryCalendarDashboardTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string Route = "/api/activity-history/dashboard/calendar";

    /// <summary>UTC+2 in July, so every offset in the summer cases below is exactly two hours.</summary>
    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    /// <summary>UTC+13 in March 2026 — NZDT runs until 5 April, so the day fold is a whole day off in UTC.</summary>
    private static readonly TimeZoneInfo Auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    /// <summary>UTC−5 in early March 2026 — EDT does not begin until the 8th, so this drifts the other way.</summary>
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // =====================================================================================================
    // 1. The day fold is the user's, not UTC (`DOC-2`)
    // =====================================================================================================

    /// <summary>
    /// East of Greenwich. 20:00Z on 10 March is 09:00 on <b>11</b> March in Auckland, and the row is
    /// inside the requested range under either reading — so this cannot pass by the range accidentally
    /// including or excluding it. The only thing it can prove is which cell the seconds land in.
    /// </summary>
    [Fact]
    public async Task Fold_PutsARowOnTheUsersCalendarDay_NotTheUtcOne_EastOfGreenwich()
    {
        await SetTimezoneAsync(Auckland);

        var (activityId, _) = await SeedActivityAsync("Auckland morning");
        await SeedCalendarDaysAsync(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 12));
        await SeedHistoryAsync(activityId, new DateTime(2026, 3, 10, 20, 0, 0, DateTimeKind.Utc));

        var days = await DaysAsync(new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 12));

        TotalSeconds(DayOn(days, new DateOnly(2026, 3, 11))).Should().Be(1800,
            "09:00 on the user's 11 March is what 2026-03-10T20:00Z reads in Auckland -- if these seconds " +
            "are on the 10th the fold used the UTC date while the day cells came from the user's calendar");
        TotalSeconds(DayOn(days, new DateOnly(2026, 3, 10))).Should().Be(0);
        days.Sum(TotalSeconds).Should().Be(1800,
            "the row is inside the range on both readings -- only the cell it lands in is in question");
    }

    /// <summary>
    /// West of Greenwich, so the drift is proven in both directions: 02:00Z on 4 March is 21:00 the
    /// <b>previous</b> local evening in New York, still on standard time that week.
    /// </summary>
    [Fact]
    public async Task Fold_PutsARowOnTheUsersCalendarDay_NotTheUtcOne_WestOfGreenwich()
    {
        await SetTimezoneAsync(NewYork);

        var (activityId, _) = await SeedActivityAsync("New York evening");
        await SeedCalendarDaysAsync(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4));
        await SeedHistoryAsync(activityId, new DateTime(2026, 3, 4, 2, 0, 0, DateTimeKind.Utc));

        var days = await DaysAsync(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4));

        TotalSeconds(DayOn(days, new DateOnly(2026, 3, 3))).Should().Be(1800,
            "21:00 on the user's 3 March is what 2026-03-04T02:00Z reads in New York -- a UTC fold credits " +
            "a user's evening to the following day, which is the same error mirrored");
        TotalSeconds(DayOn(days, new DateOnly(2026, 3, 4))).Should().Be(0);
        days.Sum(TotalSeconds).Should().Be(1800);
    }

    // =====================================================================================================
    // 2. The range's own edges
    // =====================================================================================================

    /// <summary>
    /// All four edges at once, each a minute either side of a boundary the user reads on their own clock:
    /// the range opens at the user's midnight on <c>startDate</c> and closes at the user's midnight opening
    /// the day after <c>endDate</c>, exclusive. Asserting one side only would pass on a range shifted
    /// wholesale by the user's offset.
    /// </summary>
    [Fact]
    public async Task Range_RunsFromTheUsersFirstMidnightToTheUsersLastOne()
    {
        await SetTimezoneAsync(Bratislava);

        var (activityId, _) = await SeedActivityAsync("Edges");
        await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));

        // Bratislava is UTC+2 in July, so the user's midnight is 22:00Z the day before.
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 14, 21, 59, 0, DateTimeKind.Utc)); // 23:59 on the 14th
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 14, 22, 0, 0, DateTimeKind.Utc));  // 00:00 on the 15th
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 17, 21, 59, 0, DateTimeKind.Utc)); // 23:59 on the 17th
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 17, 22, 0, 0, DateTimeKind.Utc));  // 00:00 on the 18th

        var days = await DaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));

        SessionCount(DayOn(days, new DateOnly(2026, 7, 15))).Should().Be(1,
            "a row starting exactly at the user's midnight opening startDate is inside the range");
        SessionCount(DayOn(days, new DateOnly(2026, 7, 16))).Should().Be(0);
        SessionCount(DayOn(days, new DateOnly(2026, 7, 17))).Should().Be(1,
            "23:59 on the user's endDate is inside it -- the upper bound is the following midnight, " +
            "exclusive, which is the right shape and must survive the zone fix");

        days.Sum(SessionCount).Should().Be(2,
            "the minute before the range and the minute after it are both outside -- four rows were seeded " +
            "and exactly two of them are the user's own 15th through 17th");
    }

    /// <summary>A range whose ends are the same day is one day, not two and not zero.</summary>
    [Fact]
    public async Task Range_OfASingleDay_ReturnsExactlyThatDay()
    {
        await SetTimezoneAsync(Bratislava);
        await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));

        var days = await DaysAsync(new DateOnly(2026, 7, 16), new DateOnly(2026, 7, 16));

        Date(days.Should().ContainSingle("a range whose ends are the same day is one day").Subject)
            .Should().Be(new DateOnly(2026, 7, 16));
    }

    /// <summary>
    /// `SEC-3`. An inverted range used to answer 200 with an empty list — indistinguishable, to a client,
    /// from a range in which nothing was logged. It is a malformed request and says so.
    /// </summary>
    [Fact]
    public async Task Range_WithEndBeforeStart_IsRejected()
    {
        var response = await PostAsync(new DateOnly(2026, 7, 17), new DateOnly(2026, 7, 15));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an inverted range is a client bug, and an empty 200 hides it as 'no activity that week'");
    }

    // =====================================================================================================
    // 3. The response is driven by the calendar days, not by the history rows
    // =====================================================================================================

    /// <summary>
    /// A client indexes the response by day cell, so every requested day the user has a calendar row for
    /// must appear — the empty ones as zeros rather than as absences.
    /// </summary>
    [Fact]
    public async Task Days_WithNoHistory_AreStillReturned_WithZeroes()
    {
        await SetTimezoneAsync(Bratislava);

        var (activityId, _) = await SeedActivityAsync("Only on the middle day");
        await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));
        // 10:00 on the user's 16th.
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc));

        var days = await DaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));

        days.Should().HaveCount(3, "the response is one entry per calendar day in the range, logged or not");
        days.Select(Date).Should().BeInAscendingOrder("the list is ordered by date and the client renders it as given");

        foreach (var empty in new[] { new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17) })
        {
            var day = DayOn(days, empty);
            TotalSeconds(day).Should().Be(0);
            SessionCount(day).Should().Be(0);
            TopRoles(day).Should().BeEmpty("a day with nothing logged has no roles to rank");
        }

        SessionCount(DayOn(days, new DateOnly(2026, 7, 16))).Should().Be(1);
    }

    // =====================================================================================================
    // 4. topN
    // =====================================================================================================

    /// <summary>
    /// The cap is per day and the ranking is by seconds, descending. Four roles with distinct totals, so
    /// a <c>Take</c> applied before the ordering — or an ordering by anything else — returns a different
    /// pair.
    /// </summary>
    [Fact]
    public async Task TopN_KeepsTheLargestRoles_InDescendingOrder()
    {
        var roleIds = await SeedFourRolesOnOneDayAsync();

        var days = await DaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15), topN: 2);
        var roles = TopRoles(days.Should().ContainSingle().Subject);

        roles.Select(RoleId).Should().Equal([roleIds[0], roleIds[1]],
            "the two largest totals are the four-hour and the three-hour role, in that order -- a Take " +
            "before the sort keeps whichever two the group enumeration happened to yield first");
        roles.Select(RoleSeconds).Should().Equal(4 * 3600L, 3 * 3600L);
    }

    /// <summary>
    /// The default lives on the request record (<c>TopN = 3</c>) and no frontend caller sends the field, so
    /// nothing else would notice it changing.
    /// </summary>
    [Fact]
    public async Task TopN_DefaultsToThree_WhenOmitted()
    {
        await SeedFourRolesOnOneDayAsync();

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            Route,
            new { startDate = new DateOnly(2026, 7, 15), endDate = new DateOnly(2026, 7, 15) },
            Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var days = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .EnumerateArray().ToList();

        TopRoles(days.Should().ContainSingle().Subject).Should().HaveCount(3,
            "four roles were logged that day and an omitted topN means three");
    }

    /// <summary>
    /// `SEC-3`. <c>topN</c> reached <c>Take()</c> unchecked: zero and negatives silently emptied the day's
    /// role list, and an arbitrarily large value was a caller-controlled fan-out. The bound is deliberate
    /// and is asserted at both of its edges below rather than merely near them.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(CalendarActivityRequestValidator.MaxTopN + 1)]
    public async Task TopN_OutsideOneToTen_IsRejected(int topN)
    {
        var response = await PostAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15), topN);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "topN is the per-day Take -- 0 and negatives answer 200 with every day's roles missing, which " +
            "reads as 'nothing logged', and an unbounded value is caller-controlled work");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(CalendarActivityRequestValidator.MaxTopN)]
    public async Task TopN_AtEitherEndOfTheAllowedRange_IsAnswered(int topN)
    {
        var response = await PostAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15), topN);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the bound is inclusive at both ends");
    }

    // =====================================================================================================
    // 5. Range bounds (`SEC-3`)
    // =====================================================================================================

    /// <summary>
    /// The span cap, asserted <b>at</b> the boundary. It is `DateRangeDto.MaxCustomRangeDays` rather than a
    /// second limit of this endpoint's own: the calendar is the same kind of read as every <c>summary/</c>
    /// range and there is no reason for a client to learn two numbers. Counted inclusively, so the two ends
    /// may be one less than that apart.
    /// </summary>
    [Fact]
    public async Task Range_LongerThanTheCustomRangeMaximum_IsRejected_AndExactlyTheMaximumIsAnswered()
    {
        var start = new DateOnly(2026, 1, 1);

        (await PostAsync(start, start.AddDays(DateRangeDto.MaxCustomRangeDays - 1)))
            .StatusCode.Should().Be(HttpStatusCode.OK,
                $"{DateRangeDto.MaxCustomRangeDays} days counted inclusively is the longest range allowed");

        (await PostAsync(start, start.AddDays(DateRangeDto.MaxCustomRangeDays)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "one day past the cap is refused rather than silently clamped -- answering a different " +
                "range than the one the request names is the failure this whole area was fixed for");

        (await PostAsync(new DateOnly(2000, 1, 1), new DateOnly(2030, 1, 1)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest,
                "a decade-wide request used to load every ActivityHistory row the caller owns, with " +
                "Activity and ActivityRole joined, into memory");
    }

    // =====================================================================================================
    // 6. Auth / IDOR
    // =====================================================================================================

    /// <summary>
    /// Both halves of the scoping. The history read filters <c>ah.UserId == userId</c> by hand <i>and</i>
    /// sits behind the global query filter; the day cells come from <c>ICalendarDayLookup</c>, which takes
    /// the user id as an argument. A second user with a calendar over the same dates and history on them
    /// would show up in the ids, the totals or both if either half were dropped.
    /// </summary>
    [Fact]
    public async Task Calendar_ReturnsOnlyTheCallersOwnDaysAndSeconds()
    {
        await SetTimezoneAsync(Bratislava);

        var otherUserId = await SeedSecondUserAsync();

        var (mineId, _) = await SeedActivityAsync("Mine");
        var (theirsId, _) = await SeedActivityAsync("Theirs", otherUserId);

        var myDayIds = await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));
        await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17), otherUserId);

        // 10:00 on the user's 16th for both of them, so a leak lands in the same cell and doubles it.
        await SeedHistoryAsync(mineId, new DateTime(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc));
        await SeedHistoryAsync(theirsId, new DateTime(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc), userId: otherUserId);

        var days = await DaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));

        days.Select(d => d.GetProperty("id").GetInt64()).Should().BeEquivalentTo(myDayIds,
            "the day cells are the caller's own calendar rows -- the other user's cover the same dates, so " +
            "an unscoped lookup returns six entries and the client renders each date twice");

        var middle = DayOn(days, new DateOnly(2026, 7, 16));
        TotalSeconds(middle).Should().Be(1800, "only the caller's half hour is theirs to see");
        SessionCount(middle).Should().Be(1, "sessionCount counts the caller's rows and no one else's");
        TopRoles(middle).Should().ContainSingle();
    }

    // =====================================================================================================
    // 7. Colour passthrough
    // =====================================================================================================

    /// <summary>
    /// The role's colour is handed over as stored. It used to be flattened with <c>?? ""</c> — an empty
    /// string where every other group-shaped response in the slice passes the value through — so a client
    /// reading the calendar could not treat colours the same way it treats the pie's.
    /// <para><c>ActivityRole.Color</c> is a <c>required string</c>, so "no colour" is not a state the model
    /// can be in; what is assertable, and what the flattening broke, is that the value arrives unaltered.</para>
    /// </summary>
    [Fact]
    public async Task TopRoles_CarryTheRolesColourUnaltered()
    {
        var (activityId, roleId) = await SeedActivityAsync("Coloured", color: "#0A0B0C");
        await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15));
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 15, 8, 0, 0, DateTimeKind.Utc));

        var days = await DaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15));
        var role = TopRoles(days.Should().ContainSingle().Subject).Should().ContainSingle().Subject;

        RoleId(role).Should().Be(roleId);
        role.GetProperty("color").GetString().Should().Be("#0A0B0C");
    }

    // =====================================================================================================
    // 8. Cross-surface total
    // =====================================================================================================

    /// <summary>
    /// The equality that can only hold if both sides bound the range the same way: the calendar's seconds,
    /// summed over its day cells, are the same period total <c>summary/pie-chart</c> reports for the same
    /// days. The two endpoints share nothing but the user's zone and the ledger, and the calendar was the
    /// one of them anchored at UTC midnights — with rows seeded on both sides of the user's first and last
    /// midnight, that difference is exactly what this catches.
    /// </summary>
    [Fact]
    public async Task TotalAcrossDays_MatchesThePieChartTotalForTheSameRange()
    {
        await SetTimezoneAsync(Bratislava);

        var (activityId, _) = await SeedActivityAsync("Across the range");
        await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17));

        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 14, 22, 0, 0, DateTimeKind.Utc)); // 00:00 on the 15th
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 16, 8, 0, 0, DateTimeKind.Utc));  // 10:00 on the 16th
        await SeedHistoryAsync(activityId, new DateTime(2026, 7, 17, 21, 59, 0, DateTimeKind.Utc)); // 23:59 on the 17th

        var calendarTotal = (await DaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 17))).Sum(TotalSeconds);

        var pieResponse = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-history/dashboard/summary/pie-chart",
            new
            {
                date = new DateOnly(2026, 7, 15),
                // The one range type that reads endDate; both ends inclusive, so this is the same three days.
                rangeType = "CustomRange",
                endDate = new DateOnly(2026, 7, 17),
                groupBy = "Activity",
                maxItems = 20
            },
            Json, CancellationToken);

        pieResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var pieTotal = (await pieResponse.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("totals").GetProperty("totalSeconds").GetInt64();

        calendarTotal.Should().Be(3 * 1800,
            "three half-hour rows sit on the user's 15th, 16th and 17th");
        calendarTotal.Should().Be(pieTotal,
            "the two dashboards answer the same days for the same user -- the calendar bounding its range " +
            "at UTC midnights drops the row at the user's first midnight and picks up whatever sits in the " +
            "user's last two hours, and the difference shows up here as a number, never as an error");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The fixture seeds the signed-in user on UTC, which is the one zone that cannot tell a UTC fold from
    /// the user's. Every case that means to prove the fold moves them somewhere else first; the base resets
    /// the database before each test, so this does not leak into the next one.
    /// </summary>
    private async Task SetTimezoneAsync(TimeZoneInfo timeZone)
    {
        await using var db = CreateDbContext();

        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId, CancellationToken);
        user.Timezone = timeZone;
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<long> SeedSecondUserAsync()
    {
        var email = $"calendar-{Guid.NewGuid():N}@example.com";

        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
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

        (await userManager.CreateAsync(user, "Test@1234!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    /// <summary>
    /// Each activity gets its own role, so a day's <c>topRoles</c> is as discriminating as its activities.
    /// Both names carry a <see cref="Guid"/>: the container is shared and <c>Activity</c> / <c>ActivityRole</c>
    /// each hold an unfiltered unique index on <c>(UserId, Name)</c>.
    /// </summary>
    private async Task<(long ActivityId, long RoleId)> SeedActivityAsync(
        string label, long userId = UserId, string color = "#334455")
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = userId, Name = $"{label} role {Guid.NewGuid():N}", Color = color };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = userId, Name = $"{label} {Guid.NewGuid():N}", RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        return (activity.Id, role.Id);
    }

    /// <summary>
    /// The day cells the response is built from, created through Planning's own helper rather than by
    /// inserting <c>Calendar</c> rows from a History test file. Returns their ids, in date order.
    /// </summary>
    private async Task<List<long>> SeedCalendarDaysAsync(DateOnly from, DateOnly to, long userId = UserId)
    {
        await using var db = CreateDbContext();

        var ids = new List<long>();
        for (var date = from; date <= to; date = date.AddDays(1))
            ids.Add(await PlanningTestSeedHelper.SeedCalendarAsync(db, date, userId, CancellationToken));

        return ids;
    }

    /// <summary>Half-hour rows unless told otherwise, so each one contributes exactly 1800 seconds.</summary>
    private async Task SeedHistoryAsync(long activityId, DateTime startUtc, int minutes = 30, long userId = UserId)
    {
        await using var db = CreateDbContext();

        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = userId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddMinutes(minutes),
            Length = new IntTime(minutes / 60, minutes % 60)
        });

        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// Four roles on the user's 15 July with four, three, two and one hour on them. Returns the role ids in
    /// descending order of total, which is the order the response must rank them in. The caller stays on
    /// UTC here — these cases are about the ranking, not the fold.
    /// </summary>
    private async Task<List<long>> SeedFourRolesOnOneDayAsync()
    {
        await SeedCalendarDaysAsync(new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15));

        var roleIds = new List<long>();
        foreach (var hours in new[] { 4, 3, 2, 1 })
        {
            var (activityId, roleId) = await SeedActivityAsync($"{hours}h");
            // Distinct starts: (UserId, ActivityId, StartTimestamp) is unique, and every row must stay
            // inside the user's 15th.
            await SeedHistoryAsync(activityId, new DateTime(2026, 7, 15, hours, 0, 0, DateTimeKind.Utc), hours * 60);
            roleIds.Add(roleId);
        }

        return roleIds;
    }

    private async Task<HttpResponseMessage> PostAsync(DateOnly startDate, DateOnly endDate, int topN = 3)
        => await CreateUserRoleClient().PostAsJsonAsync(
            Route, new { startDate, endDate, topN }, Json, CancellationToken);

    private async Task<List<JsonElement>> DaysAsync(DateOnly startDate, DateOnly endDate, int topN = 3)
    {
        var response = await PostAsync(startDate, endDate, topN);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .EnumerateArray()
            .ToList();
    }

    private static JsonElement DayOn(List<JsonElement> days, DateOnly date) =>
        days.Should().ContainSingle(d => Date(d) == date, $"the response must carry a cell for {date:yyyy-MM-dd}").Subject;

    private static DateOnly Date(JsonElement day) => DateOnly.Parse(day.GetProperty("date").GetString()!);
    private static long TotalSeconds(JsonElement day) => day.GetProperty("totalSeconds").GetInt64();
    private static int SessionCount(JsonElement day) => day.GetProperty("sessionCount").GetInt32();
    private static List<JsonElement> TopRoles(JsonElement day) => day.GetProperty("topRoles").EnumerateArray().ToList();
    private static long RoleId(JsonElement role) => role.GetProperty("roleId").GetInt64();
    private static long RoleSeconds(JsonElement role) => role.GetProperty("totalSeconds").GetInt64();
}
