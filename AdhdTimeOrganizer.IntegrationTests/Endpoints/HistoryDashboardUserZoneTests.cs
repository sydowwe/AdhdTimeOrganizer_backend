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
}
