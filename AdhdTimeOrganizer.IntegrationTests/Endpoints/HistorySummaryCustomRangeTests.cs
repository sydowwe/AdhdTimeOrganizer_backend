using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.application.dto.dto;
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
/// Pins <c>rangeType: "CustomRange"</c> across all four <c>summary/</c> endpoints: the range is
/// <c>date</c> through <c>endDate</c> <b>inclusive</b>, in the user's own zone, and a request that cannot
/// name a range is refused rather than answered.
///
/// <para>Every assertion here is one an endpoint can fail while still returning 200 with a well-formed,
/// plausible-looking body — which is the whole reason the defect this file covers survived: the range
/// resolver fell through to <c>(date, date)</c> and a <c>to &lt;= from</c> guard widened that to two days,
/// so a March-long request was answered for 1–2 March under a header naming March. The client cannot see
/// that, so the seeded rows below are placed where a wrong-but-plausible range still fails:</para>
///
/// <list type="bullet">
/// <item><b>the last day of the range carries a row</b> — the only row a two-day answer drops, and the one
/// that makes <c>endDate</c> demonstrably read;</item>
/// <item><b>the day after <c>endDate</c> carries one too</b> — so "inclusive" cannot pass by being
/// exclusive-plus-a-day, and neither can a range that runs to the wrong midnight;</item>
/// <item><b>both edge rows sit inside the hour where the user's day and UTC's disagree</b> (Bratislava is
/// UTC+1 in early March and UTC+2 after the 29th), so a UTC-anchored range picks up the wrong one of
/// them.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
public class HistorySummaryCustomRangeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string StackedBarsRoute = "/api/activity-history/dashboard/summary/stacked-bars";
    private const string PieChartRoute = "/api/activity-history/dashboard/summary/pie-chart";
    private const string SummaryCardsRoute = "/api/activity-history/dashboard/summary/summary-cards";
    private const string TimeOfDayRoute = "/api/activity-history/dashboard/summary/time-of-day";

    /// <summary>
    /// The fifth range-taking route in the slice. It does not carry a <see cref="DateRangeDto"/> — it has
    /// its own <c>startDate</c>/<c>endDate</c> pair — so it is not covered by the per-route theories above
    /// and gets its own guard below.
    /// </summary>
    private const string CalendarRoute = "/api/activity-history/dashboard/calendar";

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    /// <summary>The ticket's own example: a user picks 1 March and 31 March and expects March.</summary>
    private static readonly DateOnly RangeStart = new(2026, 3, 1);

    private static readonly DateOnly RangeEnd = new(2026, 3, 31);

    private const int DaysInMarch = 31;

    /// <summary>Minutes of the five seeded rows that fall inside 1–31 March on the user's clock.</summary>
    private const long InsideSeconds = (10 + 60 + 20) * 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The regression itself, on the endpoint whose response says out loud how many days it answered for.
    /// <c>daysInRange</c> of 2 is what the bug produced; anything other than 31 means <c>endDate</c> was not
    /// the range end.
    /// </summary>
    [Fact]
    public async Task TimeOfDay_CustomRange_SpansDateThroughEndDateInclusive()
    {
        await SeedTheFiveRowsAsync();

        var body = await PostAsync(TimeOfDayRoute, CustomRange());

        body.GetProperty("daysInRange").GetInt32().Should().Be(DaysInMarch,
            "1 through 31 March inclusive is 31 days -- the two-day fallback reported 2 here and the client " +
            "has no other field to notice it from");

        body.GetProperty("daysWithActivity").GetInt32().Should().Be(3,
            "the 1st, the 2nd and the 31st carry rows; the 28 February and 1 April rows are outside");

        HourTotals(body).Sum().Should().Be(InsideSeconds);
    }

    /// <summary>
    /// The three shipped panels, which have no day count in their responses at all — the only way a client
    /// could ever have caught this was by knowing what its own data summed to.
    /// </summary>
    [Theory]
    [InlineData(PieChartRoute)]
    [InlineData(SummaryCardsRoute)]
    [InlineData(StackedBarsRoute)]
    public async Task ShippedPanels_CustomRange_TotalTheWholeRange(string route)
    {
        await SeedTheFiveRowsAsync();

        var body = await PostAsync(route, CustomRange());

        TotalSecondsOf(route, body).Should().Be(InsideSeconds,
            "a two-day range starting at date would drop the 31 March row and total 70 minutes, which is a " +
            "perfectly well-formed answer to a question the user did not ask");
    }

    /// <summary>
    /// Inclusivity, stated as the two facts that distinguish it: the last picked day is in, and the day
    /// after it is not. A client sending <c>endDate + 1</c> (the exclusive reading) would fail the second
    /// assertion, which is what makes this the answer to "which convention is it".
    /// </summary>
    [Fact]
    public async Task CustomRange_IncludesEndDate_AndStopsAtTheUsersMidnightAfterIt()
    {
        var activityId = await SeedActivityAsync("Edges");

        await SeedHistoryAsync(
            // 31 March 23:00 local (CEST, UTC+2) -- the last picked day, 20 minutes.
            (activityId, new DateTime(2026, 3, 31, 21, 0, 0, DateTimeKind.Utc), new IntTime(0, 20)),
            // 1 April 00:30 local -- half an hour past the range's end. Still 31 March in UTC, so a range
            // ending at a UTC midnight swallows it.
            (activityId, new DateTime(2026, 3, 31, 22, 30, 0, DateTimeKind.Utc), new IntTime(0, 30)));

        var body = await PostAsync(TimeOfDayRoute, CustomRange());

        HourTotals(body).Sum().Should().Be(20 * 60,
            "endDate is the last day the range covers, and the range ends at the user's midnight opening " +
            "the day after it -- not at UTC's, and not a day later");

        HourTotals(body)[23].Should().Be(20 * 60, "23:00 local is hour 23 for a user in Bratislava");
    }

    /// <summary>
    /// <c>date == endDate</c> is one day. The old widening guard fired exactly here, turning the narrowest
    /// range the picker can express into two days — the case where a silently doubled range is least
    /// visible, because a single day of history looks like a plausible two days of it.
    /// </summary>
    [Fact]
    public async Task CustomRange_OfASingleDay_IsOneDay_NotTwo()
    {
        var activityId = await SeedActivityAsync("One day");

        await SeedHistoryAsync(
            (activityId, new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc), new IntTime(0, 30)),
            // The next day, which a two-day range would pull in.
            (activityId, new DateTime(2026, 3, 11, 9, 0, 0, DateTimeKind.Utc), new IntTime(0, 45)));

        var singleDay = new DateOnly(2026, 3, 10);
        var body = await PostAsync(TimeOfDayRoute, new Range(singleDay, "CustomRange", singleDay));

        body.GetProperty("daysInRange").GetInt32().Should().Be(1);
        HourTotals(body).Sum().Should().Be(30 * 60, "only the 10th is in range");
    }

    /// <summary>
    /// The other half of the contract: a <c>CustomRange</c> that names no usable range — no end, an end
    /// before its start, or an end further off than <see cref="DateRangeDto.MaxCustomRangeDays"/> — is
    /// refused on every one of the four routes, rather than quietly answered for some other range.
    ///
    /// <para>The per-route theory is the guard for the mechanism, not just the rule — FastEndpoints
    /// resolves validators by exact request type, so a route whose request type has no concrete validator
    /// skips validation entirely and answers 200. That is invisible in a test that only checks one
    /// route.</para>
    /// </summary>
    [Theory]
    [InlineData(TimeOfDayRoute)]
    [InlineData(PieChartRoute)]
    [InlineData(SummaryCardsRoute)]
    [InlineData(StackedBarsRoute)]
    public async Task EveryFourthSummaryEndpoint_RejectsACustomRangeItWillNotAnswer(string route)
    {
        var missing = await PostRawAsync(route, BodyFor(route, new Range(RangeStart, "CustomRange")));

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a CustomRange without an endDate has no end -- answering it for some default span is how the " +
            "wrong number reached the screen in the first place");

        var inverted = await PostRawAsync(route, BodyFor(route, new Range(RangeEnd, "CustomRange", RangeStart)));

        inverted.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "endDate before date is a range the client can guard against before sending");

        var tooLong = await PostRawAsync(route, BodyFor(route, SpanningDays(DateRangeDto.MaxCustomRangeDays + 1)));

        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "these endpoints read the period's rows into memory to aggregate them, so an uncapped custom " +
            "range is an uncapped read -- and summary-cards does it three times per request");
    }

    /// <summary>
    /// The cap is a limit, not an off-by-one: the longest range the client may ask for is answered, and one
    /// day more is refused. Asserted together because a cap that rejects its own boundary is indisputably a
    /// bug on the client's side of the wire and looks identical to a correct one from the server's.
    /// </summary>
    [Fact]
    public async Task CustomRange_OfExactlyTheMaximumSpan_IsAnswered_AndOneDayMoreIsNot()
    {
        var longest = await PostRawAsync(
            TimeOfDayRoute, BodyFor(TimeOfDayRoute, SpanningDays(DateRangeDto.MaxCustomRangeDays)));

        longest.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await longest.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
        body.GetProperty("daysInRange").GetInt32().Should().Be(DateRangeDto.MaxCustomRangeDays,
            "the span is counted inclusively -- both ends are days the range covers");

        var overLongest = await PostRawAsync(
            TimeOfDayRoute, BodyFor(TimeOfDayRoute, SpanningDays(DateRangeDto.MaxCustomRangeDays + 1)));

        overLongest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// <c>endDate</c> is read by <c>CustomRange</c> and by nothing else. The frontend keeps the field in the
    /// URL across range-type changes, so a stale value must neither move a named range's bounds nor 400 it.
    /// </summary>
    [Fact]
    public async Task NamedRangeTypes_IgnoreEndDate_RatherThanReadingOrRejectingIt()
    {
        await SeedTheFiveRowsAsync();

        var withStaleEndDate = await PostAsync(
            TimeOfDayRoute, new Range(RangeEnd, "ThreeDays", new DateOnly(2027, 1, 1)));

        var without = await PostAsync(TimeOfDayRoute, new Range(RangeEnd, "ThreeDays"));

        withStaleEndDate.GetProperty("daysInRange").GetInt32().Should().Be(4,
            "ThreeDays is the picked day and the three before it, whatever endDate says");
        HourTotals(withStaleEndDate).Sum().Should().Be(HourTotals(without).Sum());
    }

    /// <summary>
    /// The same cap on the calendar dashboard, which reaches it by a different road: its request is a plain
    /// <c>startDate</c>/<c>endDate</c> pair rather than a <see cref="DateRangeDto"/>, so
    /// <c>CalendarActivityRequestValidator</c> restates the rules instead of reusing
    /// <c>ApplyValidationRules</c>. Without a concrete validator FastEndpoints skips validation silently and
    /// the route answers 200 for a decade, loading every <c>ActivityHistory</c> row the caller owns —
    /// with <c>Activity</c> and <c>ActivityRole</c> joined — into memory.
    ///
    /// <para><c>topN</c> is in here because it is the per-day <c>Take</c>, and it was caller-controlled and
    /// unbounded for the same reason the range was.</para>
    /// </summary>
    [Fact]
    public async Task Calendar_RejectsARangeItWillNotAnswer_AndAnUnboundedTopN()
    {
        var ok = await PostRawAsync(CalendarRoute, CalendarBody(RangeStart, RangeEnd));
        ok.StatusCode.Should().Be(HttpStatusCode.OK, "a well-formed March request is still answered");

        var longest = await PostRawAsync(
            CalendarRoute, CalendarBody(RangeStart, RangeStart.AddDays(DateRangeDto.MaxCustomRangeDays - 1)));
        longest.StatusCode.Should().Be(HttpStatusCode.OK, "the span is counted inclusively, as it is above");

        var inverted = await PostRawAsync(CalendarRoute, CalendarBody(RangeEnd, RangeStart));
        inverted.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an inverted range currently yields an empty answer, which is indistinguishable from a user " +
            "who logged nothing");

        var tooLong = await PostRawAsync(
            CalendarRoute, CalendarBody(RangeStart, RangeStart.AddDays(DateRangeDto.MaxCustomRangeDays)));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var topNZero = await PostRawAsync(CalendarRoute, CalendarBody(RangeStart, RangeEnd, 0));
        topNZero.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var topNTooLarge = await PostRawAsync(CalendarRoute, CalendarBody(RangeStart, RangeEnd, 11));
        topNTooLarge.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Five rows around the March window: one just inside each edge, one just outside each edge, and one
    /// safely in the middle. All four edge rows sit in the hour where the user's date and UTC's differ.
    /// </summary>
    private async Task SeedTheFiveRowsAsync()
    {
        var activityId = await SeedActivityAsync("March");

        await SeedHistoryAsync(
            // 28 February 23:00 local (CET, UTC+1) -- before the range.
            (activityId, new DateTime(2026, 2, 28, 22, 0, 0, DateTimeKind.Utc), new IntTime(0, 45)),
            // 1 March 00:30 local -- the first day of the range, and still February in UTC.
            (activityId, new DateTime(2026, 2, 28, 23, 30, 0, DateTimeKind.Utc), new IntTime(0, 10)),
            // 2 March 10:00 local -- the middle, in range under any reading.
            (activityId, new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc), new IntTime(1, 0)),
            // 31 March 23:00 local (CEST, UTC+2) -- the last day of the range.
            (activityId, new DateTime(2026, 3, 31, 21, 0, 0, DateTimeKind.Utc), new IntTime(0, 20)),
            // 1 April 00:30 local -- past the range, still 31 March in UTC.
            (activityId, new DateTime(2026, 3, 31, 22, 30, 0, DateTimeKind.Utc), new IntTime(0, 30)));
    }

    /// <summary>The range fields alone — everything else a given route needs is added by <see cref="BodyFor"/>.</summary>
    private record Range(DateOnly Date, string RangeType, DateOnly? EndDate = null);

    private static Range CustomRange() => new(RangeStart, "CustomRange", RangeEnd);

    /// <summary>A custom range covering exactly <paramref name="days"/> days, both ends counted.</summary>
    private static Range SpanningDays(int days) =>
        new(RangeStart, "CustomRange", RangeStart.AddDays(days - 1));

    /// <summary>
    /// The three grouped endpoints need <c>groupBy</c>, and stacked-bars a window on top; time-of-day takes
    /// the range and nothing else. Whole-day windows keep stacked-bars' total comparable to the others'.
    /// </summary>
    private static object BodyFor(string route, Range range) => route switch
    {
        TimeOfDayRoute => new { date = range.Date, rangeType = range.RangeType, endDate = range.EndDate },
        StackedBarsRoute => new
        {
            date = range.Date,
            rangeType = range.RangeType,
            endDate = range.EndDate,
            groupBy = "Activity",
            // >= 1440 selects whole-day bands, which ignore the window times below.
            windowMinutes = 1440,
            windowStartTime = new { hours = 0, minutes = 0 },
            windowEndTime = new { hours = 0, minutes = 0 }
        },
        _ => new
        {
            date = range.Date, rangeType = range.RangeType, endDate = range.EndDate, groupBy = "Activity"
        }
    };

    private static long TotalSecondsOf(string route, JsonElement body) => route switch
    {
        PieChartRoute => body.GetProperty("totals").GetProperty("totalSeconds").GetInt64(),
        SummaryCardsRoute => body.GetProperty("periodComparison").GetProperty("currentPeriodTotalSeconds")
            .GetInt64(),
        _ => body.GetProperty("windows").EnumerateArray()
            .SelectMany(w => w.GetProperty("items").EnumerateArray())
            .Sum(i => i.GetProperty("totalSeconds").GetInt64())
    };

    private static object CalendarBody(DateOnly startDate, DateOnly endDate, int topN = 3) =>
        new { startDate, endDate, topN };

    private static List<long> HourTotals(JsonElement body) => body.GetProperty("hours").EnumerateArray()
        .Select(h => h.GetProperty("totalSeconds").GetInt64()).ToList();

    private async Task<JsonElement> PostAsync(string route, Range range)
    {
        var response = await PostRawAsync(route, BodyFor(route, range));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }

    private Task<HttpResponseMessage> PostRawAsync(string route, object body) =>
        CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);

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
