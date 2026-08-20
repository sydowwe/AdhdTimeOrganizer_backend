using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the four fragmentation measures of the <c>focus-metrics</c> dashboards. Every definition in the
/// contract has a plausible alternative reading, and <b>every one of those readings also returns 200
/// with a well-formed body</b> — so a route check proves nothing here and each case below asserts on
/// the number.
///
/// <para>The traps, in the order they are covered: a switch counted between two records of one
/// continuous run; a block broken by a detour it was supposed to tolerate, or tolerating one it was
/// not; a gap measured from the edge of the window rather than between two sessions; a mean where the
/// contract says median; and — the one the contract calls out as most likely to go wrong — a block or
/// a gap computed across the night between two days' windows, which a flat pool of the span's sessions
/// produces silently.</para>
///
/// <para>Desktop is the source under test because its ledger is the one the seed helper already builds
/// minute rows for; the calculation itself is shared by all three, which is the point of
/// <c>BaseFocusMetricsEndpoint</c>. The user's zone is the fixture default, UTC.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingFocusMetricsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string Route = "/api/activity-tracking/desktop/focus-metrics";

    private static readonly DateOnly Day1 = new(2026, 6, 1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// One continuous ten-minute run is one session and no switches. The contract is explicit that a
    /// tracker splitting a run into several records must not read as several switches — and this ledger
    /// stores one row per minute, so "several records" is the normal case, not the edge case.
    /// </summary>
    [Fact]
    public async Task ASingleUnbrokenRun_IsOneSessionAndNoSwitches()
    {
        await SeedRunAsync("chrome", Day1, new TimeOnly(9, 0), 10);

        var metrics = await PostAsync(Body(Day1, Day1));

        metrics.GetProperty("sessionCount").GetInt32().Should().Be(1,
            "ten one-minute rows on one process are one session, not ten");
        metrics.GetProperty("switchCount").GetInt32().Should().Be(0,
            "consecutive records on the same item are not a switch");
        metrics.GetProperty("longestGapSeconds").ValueKind.Should().Be(JsonValueKind.Null,
            "a single session has nothing interior to gap");
        metrics.GetProperty("daysWithActivity").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// The switch, median and label assertions on a day with a real detour in it: ten minutes of one
    /// thing, five of another, ten of the first.
    /// </summary>
    [Fact]
    public async Task SwitchesAndMedian_ReadTheDayAsThreeSessions()
    {
        await SeedDetourDayAsync();

        var metrics = await PostAsync(Body(Day1, Day1));

        metrics.GetProperty("sessionCount").GetInt32().Should().Be(3);
        metrics.GetProperty("switchCount").GetInt32().Should().Be(2,
            "chrome -> code -> chrome is two points where the foreground item differs from the one before");
        metrics.GetProperty("medianSessionSeconds").GetDouble().Should().Be(600,
            "the median of 600/300/600 is 600 -- the mean is 500, and the distribution this measure " +
            "exists to describe is exactly the one a mean misreads");
    }

    /// <summary>
    /// The tolerance is the client's to choose and has to travel with the request. The same day reads
    /// as one twenty-five-minute block or as two ten-minute ones depending on whether the five-minute
    /// detour is inside the tolerance — so a server-side constant of 120 would make the same day
    /// disagree with the client-computed figure shown beside it.
    /// </summary>
    [Fact]
    public async Task LongestBlock_HonoursTheFocusGapSecondsTheClientSends()
    {
        await SeedDetourDayAsync();

        var tolerant = await PostAsync(Body(Day1, Day1, focusGapSeconds: 300));
        var block = tolerant.GetProperty("longestBlock");

        block.GetProperty("seconds").GetDouble().Should().Be(1500,
            "the two chrome runs are separated by exactly five minutes of another item, which the " +
            "tolerance allows, so they are one block -- and its seconds are wall clock across the " +
            "detour, not the sum of the two runs");
        // Zone-less, like the timeline sessions these are derived from: the block's bounds are read
        // back off the ledger rather than resolved through the zone the way a stacked-bars band is.
        block.GetProperty("startedAt").GetDateTime().Should().Be(new DateTime(2026, 6, 1, 9, 0, 0));
        block.GetProperty("endedAt").GetDateTime().Should().Be(new DateTime(2026, 6, 1, 9, 25, 0));
        block.GetProperty("label").GetString().Should().Be("Google Chrome",
            "the label is the display name summary-cards shows for the same item -- the product name, " +
            "not the process name it is keyed on");

        var strict = await PostAsync(Body(Day1, Day1, focusGapSeconds: 120));

        strict.GetProperty("longestBlock").GetProperty("seconds").GetDouble().Should().Be(600,
            "at two minutes' tolerance the five-minute detour breaks the block, leaving the two " +
            "ten-minute runs -- ties fall to the earlier one");
        strict.GetProperty("longestBlock").GetProperty("startedAt").GetDateTime().Should()
            .Be(new DateTime(2026, 6, 1, 9, 0, 0));
    }

    /// <summary>
    /// Interior only. The stretch before the first session and the stretch after the last are the edges
    /// of the requested window, not breaks in anything — and counting the trailing one would report the
    /// remainder of an unfinished day as that day's longest break, which on a day queried at lunchtime
    /// is most of it.
    /// </summary>
    [Fact]
    public async Task LongestGap_IsMeasuredBetweenSessionsAndNotFromTheEdgesOfTheWindow()
    {
        await SeedRunAsync("chrome", Day1, new TimeOnly(9, 0), 2);
        await SeedRunAsync("code", Day1, new TimeOnly(10, 0), 3);
        await SeedRunAsync("chrome", Day1, new TimeOnly(11, 0), 2);

        var metrics = await PostAsync(Body(Day1, Day1));

        metrics.GetProperty("longestGapSeconds").GetDouble().Should().Be(3480,
            "09:02 -> 10:00 is 58 minutes and 10:03 -> 11:00 is 57 -- while the nine hours before the " +
            "first session and the nearly thirteen after the last are window edges, and either one " +
            "would win if the measure ran to the bounds of the request");
    }

    /// <summary>
    /// The trap the contract singles out. Two days, a 09:00–17:00 window, the same two minutes of the
    /// same app on each. Pool the span's sessions flat and the night between them becomes a
    /// twenty-four-hour "longest break" and the two runs join into a block that ran through it.
    /// </summary>
    [Fact]
    public async Task RangeSpans_DoNotMeasureBlocksOrGapsAcrossTheNightBetweenTwoDaysWindows()
    {
        await SeedRunAsync("chrome", Day1, new TimeOnly(9, 0), 2);
        await SeedRunAsync("chrome", Day1.AddDays(1), new TimeOnly(9, 0), 2);

        // Three days, the third deliberately empty, and a tolerance far larger than the two-minute
        // sessions -- so nothing but the day boundary can stop the two runs joining.
        var metrics = await PostAsync(Body(Day1, Day1.AddDays(2), focusGapSeconds: 3600, fromHour: 9, toHour: 17));

        metrics.GetProperty("sessionCount").GetInt32().Should().Be(2);
        metrics.GetProperty("daysWithActivity").GetInt32().Should().Be(2,
            "the third day has no activity, and the client divides the span figures by this rather " +
            "than by the length of the span so days away do not dilute them");
        metrics.GetProperty("switchCount").GetInt32().Should().Be(0,
            "one item on each of two days is not a switch between them -- the user did not switch " +
            "overnight, the dashboard simply stopped looking");
        metrics.GetProperty("longestBlock").GetProperty("seconds").GetDouble().Should().Be(120,
            "a block is bounded by the day's window: an hour of tolerance must not stitch two days' " +
            "runs into one sixteen-hour block through the excluded night");
        metrics.GetProperty("longestGapSeconds").ValueKind.Should().Be(JsonValueKind.Null,
            "neither day holds two sessions, so there is no interior gap anywhere in the span -- the " +
            "sixteen hours between one day's `to` and the next day's `from` are hours the user excluded");
    }

    /// <summary>
    /// The comparison half. A count scales with the length of the span, exactly as
    /// <c>summary-cards</c> scales its seconds — the two sit on the same screen, and disagreeing about
    /// what "compared to last 7 days" means is worse than either answer.
    /// </summary>
    [Fact]
    public async Task Baseline_ScalesTheSwitchCountToTheLengthOfTheSpanAndAveragesTheRest()
    {
        // Seven lookback days, each with the same chrome -> code -> chrome shape: two switches, session
        // lengths 120/180/120, a longest block of 180 and a longest interior gap of 3480.
        foreach (var offset in Enumerable.Range(1, 7))
            await SeedDetourShapeAsync(Day1.AddDays(-offset));

        await SeedDetourShapeAsync(Day1);

        var single = await PostAsync(Body(Day1, Day1, baseline: "last7Days"));
        var baseline = single.GetProperty("baseline");

        baseline.GetProperty("switchCount").GetDouble().Should().Be(2,
            "fourteen switches over a seven-day lookback is two on the mean day, and the span is one day");
        baseline.GetProperty("medianSessionSeconds").GetDouble().Should().Be(120,
            "the median over all twenty-one lookback sessions -- a scale-free statistic, so it is not " +
            "multiplied out to the span the way the count is");
        baseline.GetProperty("longestBlockSeconds").GetDouble().Should().Be(180,
            "the mean of each day's own longest block, not the longest block in the whole lookback -- " +
            "the latter is a record to beat, which is the framing this dashboard avoids");
        baseline.GetProperty("longestGapSeconds").GetDouble().Should().Be(3480,
            "the mean of each day's own longest interior gap");

        var span = await PostAsync(Body(Day1, Day1.AddDays(2), baseline: "last7Days"));

        span.GetProperty("baseline").GetProperty("switchCount").GetDouble().Should().Be(6,
            "two switches on the mean day, over a three-day span -- comparing a span total against an " +
            "unscaled per-day mean would report every ordinary week as up several hundred percent");
        span.GetProperty("baseline").GetProperty("longestBlockSeconds").GetDouble().Should().Be(180,
            "a per-day maximum does not scale with the span the way a count does");
    }

    /// <summary>
    /// Two ways of getting no comparison, both of which the client renders as no comparison rather than
    /// as a zero: not asking for one, and asking for one there is no history for.
    /// </summary>
    [Fact]
    public async Task Baseline_IsNullWhenNotAskedForAndWhenThereIsNoHistory()
    {
        await SeedDetourShapeAsync(Day1);

        var unasked = await PostAsync(Body(Day1, Day1));
        unasked.GetProperty("baseline").ValueKind.Should().Be(JsonValueKind.Null,
            "no baseline in the request is no comparison, not a default one");

        var noHistory = await PostAsync(Body(Day1, Day1, baseline: "last7Days"));
        noHistory.GetProperty("baseline").ValueKind.Should().Be(JsonValueKind.Null,
            "nothing was seeded before the span, and an empty lookback is a valid null rather than a " +
            "baseline of zero switches -- which would render as 'typically 0' on a new user's first day");
    }

    /// <summary>
    /// An empty span answers rather than failing, and answers with the zeros and nulls the contract
    /// names. This is the first thing a new user's dashboard asks for.
    /// </summary>
    [Fact]
    public async Task AnEmptySpan_AnswersWithZerosAndNulls()
    {
        var metrics = await PostAsync(Body(Day1, Day1));

        metrics.GetProperty("sessionCount").GetInt32().Should().Be(0);
        metrics.GetProperty("daysWithActivity").GetInt32().Should().Be(0);
        metrics.GetProperty("switchCount").GetInt32().Should().Be(0);
        metrics.GetProperty("longestGapSeconds").ValueKind.Should().Be(JsonValueKind.Null);
        metrics.GetProperty("longestBlock").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Unlike the timeline, a range is the point — <c>U3</c> left the timeline single-day, and that is
    /// precisely why these numbers have to be computed here for a span at all.
    /// </summary>
    [Fact]
    public async Task ASpan_IsAccepted_UnlikeTheTimeline()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            Route, Body(Day1, Day1.AddDays(6)), Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>Ten minutes of chrome, five of code, ten of chrome — the shape three cases read.</summary>
    private async Task SeedDetourDayAsync()
    {
        await SeedRunAsync("chrome", Day1, new TimeOnly(9, 0), 10);
        await SeedRunAsync("code", Day1, new TimeOnly(9, 10), 5);
        await SeedRunAsync("chrome", Day1, new TimeOnly(9, 15), 10);
    }

    /// <summary>
    /// Two minutes, an hour's break, three minutes of something else, an hour's break, two minutes.
    /// Two switches, a longest interior gap of 3480 and — at the default two-minute tolerance — a
    /// longest block of 180. The middle run is three minutes rather than two on purpose: the timeline's
    /// context-switch absorption merges around an interruption of two minutes or less, which would make
    /// this one day of chrome and no switches at all.
    /// </summary>
    private async Task SeedDetourShapeAsync(DateOnly day)
    {
        await SeedRunAsync("chrome", day, new TimeOnly(9, 0), 2);
        await SeedRunAsync("code", day, new TimeOnly(10, 0), 3);
        await SeedRunAsync("chrome", day, new TimeOnly(11, 0), 2);
    }

    /// <summary>
    /// <paramref name="minutes"/> consecutive one-minute rows of sixty active seconds each, which the
    /// session builder stitches into a single run.
    /// </summary>
    private async Task SeedRunAsync(string processName, DateOnly day, TimeOnly start, int minutes)
    {
        await using var db = CreateDbContext();

        for (var i = 0; i < minutes; i++)
            db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
            {
                UserId = UserId,
                WindowStart = DateTime.SpecifyKind(day.ToDateTime(start).AddMinutes(i), DateTimeKind.Utc),
                ProcessName = processName,
                ProductName = processName == "chrome" ? "Google Chrome" : "Visual Studio Code",
                WindowTitle = "t",
                ExecutablePath = $"/usr/bin/{processName}",
                IsFullscreen = false,
                ActiveSeconds = 60,
                BackgroundSeconds = 0,
                IsPlayingSound = false,
                ActiveMonitor = 0
            });

        await db.SaveChangesAsync(CancellationToken);
    }

    private static object Body(
        DateOnly dateFrom,
        DateOnly dateTo,
        int focusGapSeconds = 120,
        string? baseline = null,
        int fromHour = 0,
        int toHour = 0) => new
    {
        dateFrom,
        dateTo,
        from = new { hours = fromHour, minutes = 0 },
        to = new { hours = toHour, minutes = 0 },
        focusGapSeconds,
        baseline
    };

    private async Task<JsonElement> PostAsync(object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(Route, body, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }
}
