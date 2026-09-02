using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the one convention the History dashboards have for a bare <c>TimeDto</c> and a bare
/// <c>DateOnly</c> off a request: <b>they are wall clock in the caller's own <c>User.Timezone</c></b>,
/// the same reading the command side already gives them (<c>PlannerTask.ActualStartTime</c> stores a
/// <c>TimeDto</c> verbatim into a <c>TimeOnly</c>) and the same one <c>PlannerStreakReader</c> uses for
/// its day boundary.
///
/// <para>Every assertion here is on <b>which rows come back</b> and <b>which instants the returned window
/// bounds name</b>, never on a status code, because none of the three ways this used to be wrong produced
/// an error:</para>
/// <list type="number">
/// <item>The detail dashboards resolved the picked window with <c>DateTime.ToUniversalTime()</c> over a
/// <see cref="DateTimeKind.Unspecified"/> value, which silently means <b>the server process's</b> zone —
/// so the same request answered differently on a developer's machine than in a UTC container, and matched
/// the user only when the two coincided.</item>
/// <item>The summary stacked-bars endpoint added <c>WindowStartTime</c> / <c>WindowEndTime</c> to a UTC
/// midnight as raw offsets, i.e. read the very same field as <b>UTC</b>. The two endpoints therefore
/// windowed the same picked hours differently, which is what the frontend saw as the summary chart's axis
/// disagreeing with the detail chart's for one set of data.</item>
/// <item>The summary endpoints' day boundaries were UTC midnights, so a user east of Greenwich had the
/// last hours of every evening credited to the following day.</item>
/// </list>
///
/// <para>The zone is <c>Europe/Bratislava</c> — the app's home zone, and one with a DST rule, so the
/// July cases run at UTC+2 and <see cref="DetailWindow_StartingInsideTheSpringForwardGap_ResolvesToTheEndOfTheGap"/>
/// exercises a wall clock that does not exist.</para>
/// </summary>
[Collection("Postgres")]
public class HistoryDashboardUserZoneTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    /// <summary>Mid-July, so Bratislava is on CEST (UTC+2) and every offset below is exactly two hours.</summary>
    private static readonly DateOnly SummerDay = new(2026, 7, 15);

    /// <summary>2026's spring-forward Sunday in the EU: local clocks jump 02:00 → 03:00, so 02:30 never happens.</summary>
    private static readonly DateOnly SpringForwardDay = new(2026, 3, 29);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The caller is a Bratislava user for every case in this class. Runs after the fixture's baseline seed,
    /// which creates the test user on UTC.
    /// </summary>
    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The core of B3. The user picks 09:00–10:00 on their own clock; in July that is 07:00–08:00 UTC.
    /// One row sits inside the window on the user's clock but outside it in UTC, and one sits the other way
    /// round — so reading the <c>TimeDto</c> as UTC does not merely shift the answer, it returns exactly the
    /// wrong row. Asserting on both directions is what makes this test unable to pass by coincidence on a
    /// server whose local zone happens to be Bratislava.
    /// </summary>
    [Fact]
    public async Task DetailDashboard_ReadsThePickedWindowInTheUsersZone_NotUtc()
    {
        var insideId = await SeedActivityAsync("Inside the user's 09:00-10:00");
        var outsideId = await SeedActivityAsync("Outside the user's 09:00-10:00");

        await SeedHistoryAsync(
            // 09:30 in Bratislava — inside the picked window, but 07:30 UTC is outside a UTC reading of it.
            (insideId, new DateTime(2026, 7, 15, 7, 30, 0, DateTimeKind.Utc)),
            // 11:30 in Bratislava — outside the picked window, but 09:30 UTC is inside a UTC reading of it.
            (outsideId, new DateTime(2026, 7, 15, 9, 30, 0, DateTimeKind.Utc)));

        var names = await PieChartNamesAsync("/api/activity-history/dashboard/detail/pie-chart", new
        {
            date = SummerDay,
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 10, minutes = 0 },
            groupBy = "Activity",
            maxItems = 20
        });

        names.Should().Contain("Inside the user's 09:00-10:00",
            "09:30 on the user's clock is inside the 09:00-10:00 they picked -- if this is missing the " +
            "endpoint read the bare TimeDto as UTC (or as the server's local zone) rather than as wall " +
            "clock in User.Timezone");
        names.Should().NotContain("Outside the user's 09:00-10:00",
            "11:30 on the user's clock is outside the window they picked -- its presence means the window " +
            "was resolved against the wrong zone, and the two assertions together rule out the server's " +
            "local zone happening to match");
    }

    /// <summary>
    /// The divergence the frontend reported: the same picked hours, sent to the two sibling endpoints, must
    /// name the same instants. The absolute value is asserted as well as the equality — two endpoints that
    /// are wrong in the same way would satisfy equality alone, and before this change both resolved
    /// 09:00 to 09:00 UTC.
    /// </summary>
    [Fact]
    public async Task SummaryAndDetailStackedBars_AgreeOnTheInstantsAPickedWindowNames()
    {
        var activityId = await SeedActivityAsync("Windowed");
        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 15, 7, 30, 0, DateTimeKind.Utc)));

        var detail = await WindowStartsAsync("/api/activity-history/dashboard/detail/stacked-bars", new
        {
            date = SummerDay,
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 10, minutes = 0 },
            groupBy = "Activity"
        });

        var summary = await WindowStartsAsync("/api/activity-history/dashboard/summary/stacked-bars", new
        {
            date = SummerDay,
            rangeType = "ThreeDays",
            groupBy = "Activity",
            windowMinutes = 60,
            windowStartTime = new { hours = 9, minutes = 0 },
            windowEndTime = new { hours = 10, minutes = 0 }
        });

        var expected = new DateTimeOffset(2026, 7, 15, 7, 0, 0, TimeSpan.Zero);

        detail.Should().StartWith(expected,
            "the detail dashboard's first hourly bar begins at the picked 09:00, which is 07:00Z in July " +
            "for a Bratislava user");
        summary.Should().Contain(expected,
            "the summary dashboard's bar for the same day and the same picked 09:00 must begin at the same " +
            "instant -- windowStartTime is the same zone-less TimeDto the detail endpoint receives, and " +
            "reading it as a raw offset from UTC midnight is what made the two charts disagree");
    }

    /// <summary>
    /// `TEST-7` / `CQ-3`. The detail dashboard used to emit a fixed 24 hourly bars from the picked start and
    /// never read the picked end, so 09:00-17:00 answered with sixteen always-empty bars, the last of them
    /// ending at 09:00 the following day. Asserting the count <b>and</b> both outer bounds is what the
    /// agreement test above could not catch: it only compared the windows that overlapped.
    /// </summary>
    [Theory]
    [InlineData(9, 17, 8)]
    // A whole day still has to come back as 24 -- a fix that simply capped the loop in the wrong place
    // would satisfy the case above and break this one.
    [InlineData(0, 0, 24)]
    // Over midnight: DateAndTimeRangeDto reads to <= from as a band running into the next date, so this is
    // 22:00 through 06:00 on the user's clock, not a zero-length or a backwards window.
    [InlineData(22, 6, 8)]
    public async Task DetailStackedBars_EmitsOneBarPerHourOfThePickedWindow_AndNoneOutsideIt(
        int fromHour, int toHour, int expectedWindows)
    {
        var windows = await WindowBoundsAsync("/api/activity-history/dashboard/detail/stacked-bars", new
        {
            date = SummerDay,
            from = new { hours = fromHour, minutes = 0 },
            to = new { hours = toHour, minutes = 0 },
            groupBy = "Activity"
        });

        // July, so Bratislava is UTC+2 throughout and the picked wall clock is the hour minus two.
        var expectedStart = new DateTimeOffset(2026, 7, 15, fromHour, 0, 0, TimeSpan.Zero).AddHours(-2);
        var expectedEnd = expectedStart.AddHours(expectedWindows);

        windows.Should().HaveCount(expectedWindows,
            "the bars span the window the user picked -- a fixed 24 ignores the picked end and pads the " +
            "chart with bars that can never hold a row");
        windows[0].Start.Should().Be(expectedStart);
        windows[^1].End.Should().Be(expectedEnd);

        windows.Should().OnlyContain(w => w.Start >= expectedStart && w.End <= expectedEnd,
            "no bar may lie outside the picked window, whatever the edge arithmetic does");
        windows.Zip(windows.Skip(1)).Should().OnlyContain(p => p.First.End == p.Second.Start,
            "the bands tile the window with no gap and no overlap");
    }

    /// <summary>
    /// The detail side's counterpart to <see cref="DetailWindow_StartingInsideTheSpringForwardGap_ResolvesToTheEndOfTheGap"/>,
    /// and the second half of `CQ-3`: the old walk added hours to a UTC instant, so on a transition day every
    /// bar after the change drifted an hour against the clock the user reads. Local 00:00-06:00 is five real
    /// hours on the spring-forward day, and the last band must still be the user's 05:00-06:00.
    /// </summary>
    [Fact]
    public async Task DetailStackedBars_OnTheSpringForwardDay_KeepTheUsersWallClock()
    {
        var windows = await WindowBoundsAsync("/api/activity-history/dashboard/detail/stacked-bars", new
        {
            date = SpringForwardDay,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 6, minutes = 0 },
            groupBy = "Activity"
        });

        windows.Should().HaveCount(5,
            "the local hour 02:00-03:00 does not exist on this date, so the band that names it is dropped " +
            "rather than emitted empty -- 00:00 to 06:00 is five hours here");

        // Local clocks jump 02:00 -> 03:00 at 01:00Z: the first two bands are UTC+1, the rest UTC+2.
        windows[0].Start.Should().Be(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero));
        windows[^1].Start.Should().Be(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.Zero),
            "the user's 05:00 is 03:00Z once the clocks have gone forward -- adding hours to the UTC start " +
            "instead lands it an hour early, which is the drift the summary side already guards against");
        windows[^1].End.Should().Be(new DateTimeOffset(2026, 3, 29, 4, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// The agreement test's stronger form. Given the same picked window at the same resolution, the two
    /// stacked-bars endpoints must return the <b>same</b> window list, not merely overlapping ones — the
    /// assertion that could not have passed while the detail side emitted 24 bars for a 09:00-13:00 pick.
    /// </summary>
    [Fact]
    public async Task SummaryAndDetailStackedBars_ReturnTheSameWindowsAtTheSameResolution()
    {
        var detail = await WindowStartsAsync("/api/activity-history/dashboard/detail/stacked-bars", new
        {
            date = SummerDay,
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 13, minutes = 0 },
            groupBy = "Activity"
        });

        var summary = await WindowStartsAsync("/api/activity-history/dashboard/summary/stacked-bars", new
        {
            date = SummerDay,
            // The one member that reads endDate; equal ends are the single day the detail request picks.
            rangeType = "CustomRange",
            endDate = SummerDay,
            groupBy = "Activity",
            windowMinutes = 60,
            windowStartTime = new { hours = 9, minutes = 0 },
            windowEndTime = new { hours = 13, minutes = 0 }
        });

        detail.Should().Equal(summary,
            "the two dashboards window a picked day identically -- the summary side clips its bands at the " +
            "picked end and the detail side must do the same, or the two charts disagree about their axis");
    }

    /// <summary>
    /// The bucketing rule itself: a row belongs to the band whose half-open <c>[Start, End)</c> contains its
    /// <c>StartTimestamp</c>, the same rule every other read in the slice applies.
    /// </summary>
    [Fact]
    public async Task DetailStackedBars_PutEachRowInTheBandItsStartFallsIn()
    {
        var earlyId = await SeedActivityAsync("09:30 half hour");
        var lateId = await SeedActivityAsync("11:30 half hour");

        // 07:30Z and 09:30Z are 09:30 and 11:30 on the user's July clock.
        await SeedHistoryAsync(
            (earlyId, new DateTime(2026, 7, 15, 7, 30, 0, DateTimeKind.Utc)),
            (lateId, new DateTime(2026, 7, 15, 9, 30, 0, DateTimeKind.Utc)));

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-history/dashboard/detail/stacked-bars",
            new
            {
                date = SummerDay,
                from = new { hours = 9, minutes = 0 },
                to = new { hours = 13, minutes = 0 },
                groupBy = "Activity"
            },
            Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var windows = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("windows").EnumerateArray()
            .Select(w => (
                Start: w.GetProperty("windowStart").GetDateTimeOffset(),
                Items: w.GetProperty("items").EnumerateArray()
                    .Select(i => (Name: i.GetProperty("name").GetString(),
                        Seconds: i.GetProperty("totalSeconds").GetDouble()))
                    .ToList()))
            .ToList();

        windows.Should().HaveCount(4);

        windows[0].Items.Should().ContainSingle()
            .Which.Should().Be(("09:30 half hour", 1800d));
        windows[1].Items.Should().BeEmpty("nothing was logged in the user's 10:00 hour");
        windows[2].Items.Should().ContainSingle()
            .Which.Should().Be(("11:30 half hour", 1800d));
        windows[3].Items.Should().BeEmpty("nothing was logged in the user's 12:00 hour");
    }

    /// <summary>
    /// The summary dashboards' day boundaries are the user's midnights. Both rows below sit in the half-open
    /// UTC hour that the two readings disagree about, one on each side, so a UTC-midnight range returns
    /// precisely the inverse of the correct set.
    /// </summary>
    [Fact]
    public async Task SummaryDashboard_BoundsTheRangeByTheUsersMidnights_NotUtcMidnights()
    {
        var firstDayId = await SeedActivityAsync("00:30 on the range's first day");
        var dayAfterId = await SeedActivityAsync("00:30 on the day after the range");

        // ThreeDays over 2026-07-15 covers the user's 12th through 15th of July inclusive.
        await SeedHistoryAsync(
            // 2026-07-12 00:30 in Bratislava — the first day of the range, but 2026-07-11 in UTC.
            (firstDayId, new DateTime(2026, 7, 11, 22, 30, 0, DateTimeKind.Utc)),
            // 2026-07-16 00:30 in Bratislava — past the end of the range, but still 2026-07-15 in UTC.
            (dayAfterId, new DateTime(2026, 7, 15, 22, 30, 0, DateTimeKind.Utc)));

        var names = await PieChartNamesAsync("/api/activity-history/dashboard/summary/pie-chart", new
        {
            date = SummerDay,
            rangeType = "ThreeDays",
            groupBy = "Activity",
            maxItems = 20
        });

        names.Should().Contain("00:30 on the range's first day",
            "half past midnight on the 12th is inside a range that starts on the user's 12th -- anchoring " +
            "the range at UTC midnight cuts the first two hours off the user's first day");
        names.Should().NotContain("00:30 on the day after the range",
            "half past midnight on the 16th is past a range that ends with the user's 15th -- a UTC " +
            "midnight bound credits the user's late evenings to the following day");
    }

    /// <summary>
    /// A wall clock the user can pick but the calendar does not contain. <c>TimeZoneInfo.ConvertTimeToUtc</c>
    /// throws <see cref="ArgumentException"/> for it, which would surface as a 500 on a date the user has
    /// every right to open, so <c>WallClockZone</c> walks forward to the instant the gap closes. The row is
    /// logged at 03:15 local — inside the requested band only once the 02:30 start has resolved to 03:00.
    /// </summary>
    [Fact]
    public async Task DetailWindow_StartingInsideTheSpringForwardGap_ResolvesToTheEndOfTheGap()
    {
        var activityId = await SeedActivityAsync("Just after the clocks went forward");

        // Local clocks read 03:15 (CEST, UTC+2) at this instant; 01:15Z is the first quarter hour that
        // exists after the 01:00Z-to-CEST jump.
        await SeedHistoryAsync((activityId, new DateTime(2026, 3, 29, 1, 15, 0, DateTimeKind.Utc)));

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-history/dashboard/detail/pie-chart",
            new
            {
                date = SpringForwardDay,
                from = new { hours = 2, minutes = 30 },
                to = new { hours = 4, minutes = 0 },
                groupBy = "Activity",
                maxItems = 20
            },
            Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "02:30 does not exist on the spring-forward day, and letting ConvertTimeToUtc throw would 500 " +
            "the dashboard for every user in a DST zone one day a year");

        var names = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString())
            .ToList();

        names.Should().Contain("Just after the clocks went forward",
            "the window opens when the gap closes, at 03:00 local, so 03:15 is inside it");
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = UserId, Name = $"{name} role", Color = "#334455" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        return activity.Id;
    }

    /// <summary>Half-hour rows, so a row that starts inside a one-hour band is wholly inside it.</summary>
    private async Task SeedHistoryAsync(params (long ActivityId, DateTime StartUtc)[] rows)
    {
        await using var db = CreateDbContext();

        db.Set<ActivityHistory>().AddRange(rows.Select(r => new ActivityHistory
        {
            UserId = UserId,
            ActivityId = r.ActivityId,
            StartTimestamp = r.StartUtc,
            EndTimestamp = r.StartUtc.AddMinutes(30),
            Length = new IntTime(0, 30)
        }));

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<List<string?>> PieChartNamesAsync(string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString())
            .ToList();
    }

    /// <summary>
    /// The returned <c>windowStart</c> values as instants. They are read as <see cref="DateTimeOffset"/>
    /// rather than <c>DateTime</c> on purpose: that only parses if the payload carries a zone designator,
    /// so these cases also pin the response half of the contract — <c>HistoryWindow.WindowStart</c> is a
    /// <c>Kind.Utc</c> value and therefore serialises with a trailing <c>Z</c>.
    /// </summary>
    private async Task<List<DateTimeOffset>> WindowStartsAsync(string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("windows").EnumerateArray()
            .Select(w => w.GetProperty("windowStart").GetDateTimeOffset())
            .ToList();
    }

    /// <summary>Both bounds of every returned band, for the cases that assert on where a window ends.</summary>
    private async Task<List<(DateTimeOffset Start, DateTimeOffset End)>> WindowBoundsAsync(
        string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("windows").EnumerateArray()
            .Select(w => (
                w.GetProperty("windowStart").GetDateTimeOffset(),
                w.GetProperty("windowEnd").GetDateTimeOffset()))
            .ToList();
    }
}
