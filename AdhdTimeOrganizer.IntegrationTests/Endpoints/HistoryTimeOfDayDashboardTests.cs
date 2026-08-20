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
/// Pins <c>POST /activity-history/dashboard/summary/time-of-day</c>, whose whole value to the client is
/// three semantics that a passing route and a plausible-looking chart say nothing about:
///
/// <list type="number">
/// <item>the fold is in the caller's <c>User.Timezone</c> — folding in UTC shifts every user's answer by
/// their offset, which is the one error that looks completely correct on screen;</item>
/// <item>a record crossing an hour boundary is split across the hours it covers by elapsed time, rather than
/// attributed whole to its start hour, which answers a different question;</item>
/// <item><c>sum(hours[].totalSeconds)</c> equals what <c>summary/pie-chart</c> reports for the same range,
/// which the frontend is told it may rely on.</item>
/// </list>
///
/// <para>The zone is <c>Europe/Bratislava</c>, on CEST (UTC+2) for every case here, so no assertion can pass
/// by the server's local zone coinciding with UTC.</para>
/// </summary>
[Collection("Postgres")]
public class HistoryTimeOfDayDashboardTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string Route = "/api/activity-history/dashboard/summary/time-of-day";

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    /// <summary>Mid-July: Bratislava is UTC+2, so every local time below is two hours after its instant.</summary>
    private static readonly DateOnly SummerDay = new(2026, 7, 15);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The client indexes <c>hours</c> by position and neither sorts nor pads it, so an empty hour must come
    /// back as a zero row rather than be omitted — one missing bucket misaligns every hour after it, silently.
    /// An empty range is the case most likely to tempt an implementation into returning only what it grouped.
    /// </summary>
    [Fact]
    public async Task Hours_AreAlwaysTwentyFourInOrder_EvenWhenNothingWasLogged()
    {
        var body = await TimeOfDayAsync();
        var hours = body.GetProperty("hours").EnumerateArray().ToList();

        hours.Should().HaveCount(24, "the contract is all 24 hours, present or not");
        hours.Select(h => h.GetProperty("hour").GetInt32()).Should().Equal(Enumerable.Range(0, 24));
        hours.Should().OnlyContain(h => h.GetProperty("totalSeconds").GetInt64() == 0);
        hours.Should().OnlyContain(h => h.GetProperty("entries").GetInt32() == 0);

        body.GetProperty("daysWithActivity").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// 23:30 on the user's clock is hour 23. In UTC the same instant is 21:30, so a UTC fold puts the whole
    /// record two buckets away — asserting on both buckets is what makes that impossible to pass by accident.
    /// </summary>
    [Fact]
    public async Task Hours_AreFoldedInTheUsersZone_NotUtc()
    {
        var activityId = await SeedActivityAsync("Late evening");
        // 2026-07-14 23:30 in Bratislava.
        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 14, 21, 30, 0, DateTimeKind.Utc), new IntTime(0, 30)));

        var hours = await HourTotalsAsync();

        hours[23].Should().Be(30 * 60,
            "the record starts at 23:30 on the user's clock -- if this is zero the endpoint folded UTC hours");
        hours[21].Should().Be(0,
            "21:30Z is the same instant, and crediting hour 21 is exactly the UTC fold this must not do");
    }

    /// <summary>
    /// The split rule, on the contract's own example — 90 minutes from 09:45 — with its arithmetic corrected:
    /// that block runs to 11:15, so it covers three hours (900s / 3600s / 900s), not the two the contract's
    /// worked figures name. The rule is what is implemented; the figures were a slip.
    ///
    /// <para>Attributing the record whole to its start hour would give 5400/0/0 — a plausible-looking chart
    /// answering "when do you start things".</para>
    /// </summary>
    [Fact]
    public async Task RecordCrossingAnHourBoundary_IsSplitByElapsedTime_AndCountsInEveryHourItTouches()
    {
        var activityId = await SeedActivityAsync("Long block");
        // 2026-07-14 09:45 in Bratislava, running 90 minutes to 11:15.
        await SeedHistoryAsync((activityId, new DateTime(2026, 7, 14, 7, 45, 0, DateTimeKind.Utc), new IntTime(1, 30)));

        var body = await TimeOfDayAsync();
        var hours = HourTotals(body);
        var entries = HourEntries(body);

        hours[9].Should().Be(15 * 60, "09:45-10:00 is a quarter of an hour of the block");
        hours[10].Should().Be(60 * 60, "the block covers the whole of hour 10");
        hours[11].Should().Be(15 * 60, "11:00-11:15 is its tail");
        hours.Sum().Should().Be(90 * 60, "the split is a partition of the record, not a duplication of it");

        entries[9].Should().Be(1);
        entries[10].Should().Be(1);
        entries[11].Should().Be(1);
        entries.Sum().Should().Be(3,
            "one record touching three hours counts once in each, so summing entries is not the period's " +
            "record count -- the frontend is told this explicitly");
    }

    /// <summary>
    /// The equality the frontend leans on when it turns a bucket into a share of the period. Deliberately
    /// seeded with the two rows most likely to break it: one crossing the user's midnight (its tail lands in
    /// hour 0, a different day, but the same 24 buckets) and one starting in the range's last half hour (its
    /// tail runs past the range's end, and both endpoints count the whole record because both select by
    /// <c>StartTimestamp</c>).
    /// </summary>
    [Fact]
    public async Task TotalAcrossHours_MatchesThePieChartTotalForTheSameRange()
    {
        var activityId = await SeedActivityAsync("Counted");

        await SeedHistoryAsync(
            // 2026-07-13 23:45 local, 30 minutes: 15 into hour 23, 15 into hour 0 of the next day.
            (activityId, new DateTime(2026, 7, 13, 21, 45, 0, DateTimeKind.Utc), new IntTime(0, 30)),
            // 2026-07-14 10:00 local, an hour.
            (activityId, new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc), new IntTime(1, 0)),
            // 2026-07-15 23:40 local, 40 minutes -- runs past the range's final midnight.
            (activityId, new DateTime(2026, 7, 15, 21, 40, 0, DateTimeKind.Utc), new IntTime(0, 40)));

        var hours = await HourTotalsAsync();

        var pie = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-history/dashboard/summary/pie-chart",
            new { date = SummerDay, rangeType = "ThreeDays", groupBy = "Activity", maxItems = 20 },
            Json, CancellationToken);
        pie.StatusCode.Should().Be(HttpStatusCode.OK);

        var pieTotal = (await pie.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken))
            .GetProperty("totals").GetProperty("totalSeconds").GetInt64();

        pieTotal.Should().Be((30 + 60 + 40) * 60, "all three rows start inside the range");
        hours.Sum().Should().Be(pieTotal,
            "the two endpoints select the same rows the same way and distribute each row's whole length, so " +
            "the 24 buckets are a partition of the period total -- a clipped tail or a dropped row shows up " +
            "here as a shortfall and nowhere else");

        hours[23].Should().Be(15 * 60 + 20 * 60, "23:45-00:00 on the 13th plus 23:40-00:00 on the 15th");
        hours[0].Should().Be(15 * 60 + 20 * 60, "the two tails, both past the user's midnight");
    }

    /// <summary>
    /// <c>daysWithActivity</c> is the client's data threshold, so it counts <b>days</b>, not records, and the
    /// days are the user's. The 22:30Z row is half past midnight on the user's next day: counting UTC days
    /// would fold it into the earlier one and under-report the threshold.
    /// </summary>
    [Fact]
    public async Task DaysWithActivity_CountsDistinctUserDays_AndDaysInRangeSpansTheWholeRange()
    {
        var activityId = await SeedActivityAsync("Spread out");

        await SeedHistoryAsync(
            // Two rows on the user's 14th.
            (activityId, new DateTime(2026, 7, 14, 6, 0, 0, DateTimeKind.Utc), new IntTime(0, 30)),
            (activityId, new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc), new IntTime(0, 30)),
            // 2026-07-15 00:30 local -- still the 14th in UTC.
            (activityId, new DateTime(2026, 7, 14, 22, 30, 0, DateTimeKind.Utc), new IntTime(0, 30)));

        var body = await TimeOfDayAsync();

        body.GetProperty("daysWithActivity").GetInt32().Should().Be(2,
            "three rows fall on two of the user's days -- one on the 14th's evening is the 15th locally");

        // ThreeDays over the 15th is the user's 12th through 15th inclusive, the same span every other
        // summary/ endpoint applies (DateRangeDto.ToDateRange).
        body.GetProperty("daysInRange").GetInt32().Should().Be(4);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<JsonElement> TimeOfDayAsync()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            Route, new { date = SummerDay, rangeType = "ThreeDays" }, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }

    private async Task<List<long>> HourTotalsAsync() => HourTotals(await TimeOfDayAsync());

    private static List<long> HourTotals(JsonElement body) => body.GetProperty("hours").EnumerateArray()
        .Select(h => h.GetProperty("totalSeconds").GetInt64()).ToList();

    private static List<int> HourEntries(JsonElement body) => body.GetProperty("hours").EnumerateArray()
        .Select(h => h.GetProperty("entries").GetInt32()).ToList();

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = UserId, Name = $"{name} role", Color = "#223344" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);

        return activity.Id;
    }

    private async Task SeedHistoryAsync(params (long ActivityId, DateTime StartUtc, IntTime Length)[] rows)
    {
        await using var db = CreateDbContext();

        db.Set<ActivityHistory>().AddRange(rows.Select(r => new ActivityHistory
        {
            UserId = UserId,
            ActivityId = r.ActivityId,
            StartTimestamp = r.StartUtc,
            EndTimestamp = r.StartUtc.AddSeconds(r.Length.TotalSeconds),
            Length = r.Length
        }));

        await db.SaveChangesAsync(CancellationToken);
    }
}
