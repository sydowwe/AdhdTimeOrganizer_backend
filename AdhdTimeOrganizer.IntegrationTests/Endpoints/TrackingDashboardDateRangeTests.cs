using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the date-range semantic of the twelve tracking dashboards: <c>dateFrom</c>/<c>dateTo</c> are an
/// inclusive span of calendar days, and <c>from</c>/<c>to</c> stay a <b>time-of-day window repeated on
/// each day in it</b> rather than becoming the ends of the range.
///
/// <para>Everything here asserts on the numbers in the response, not on the route answering. Read the
/// span as one continuous block instead and every endpoint still returns 200 with a well-formed body —
/// the totals are just silently inflated by every night the user's window was supposed to exclude, and
/// on a narrowed working-day window that is most of what comes back.</para>
///
/// <para>The user's zone is the fixture default, UTC, so the instants below read the same on both
/// sides. <see cref="TrackingDashboardUserZoneTests"/> is where the zone itself is exercised.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingDashboardDateRangeTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Day1 = new(2026, 6, 1);
    private static readonly DateOnly Day3 = new(2026, 6, 3);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The whole point of the daily-window rule. Three days, a 09:00–17:00 window, and one row sitting
    /// at half past three in the morning on the middle night. A continuous reading of the span swallows
    /// that row; the repeating window does not.
    /// </summary>
    [Fact]
    public async Task PieChart_RepeatsTheTimeOfDayWindowOnEachDay_RatherThanSpanningTheRangeContinuously()
    {
        await SeedMinutesAsync(
            Day1.ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(1).ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(2).ToDateTime(new TimeOnly(12, 0)),
            // 03:30 on the second night — inside the span's outer envelope, outside every daily window.
            Day1.AddDays(1).ToDateTime(new TimeOnly(3, 30)));

        var response = await PostAsync("/api/activity-tracking/desktop/pie-chart", new
        {
            dateFrom = Day1,
            dateTo = Day3,
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 17, minutes = 0 }
        });

        response.GetProperty("totals").GetProperty("totalSeconds").GetInt32().Should().Be(180,
            "three midday rows of sixty seconds are inside the 09:00-17:00 window on their own days, " +
            "and the 03:30 row is inside the span's envelope but inside no day's window -- reading " +
            "dateFrom/dateTo as one continuous block folds every night into the totals");
    }

    /// <summary>
    /// The count-style totals must be distinct over the whole span, not summed per day. Invisible on a
    /// single day and inflated by the length of the span on any other.
    /// </summary>
    [Fact]
    public async Task PieChart_CountsAProcessSeenOnEveryDayOnce()
    {
        await SeedMinutesAsync(
            Day1.ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(1).ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(2).ToDateTime(new TimeOnly(12, 0)));

        var response = await PostAsync("/api/activity-tracking/desktop/pie-chart", new
        {
            dateFrom = Day1,
            dateTo = Day3,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 }
        });

        response.GetProperty("totals").GetProperty("totalProcesses").GetInt32().Should().Be(1,
            "one process ran on all three days, so it is one process -- a per-day count summed over " +
            "the span reports three");
    }

    /// <summary>
    /// A band is keyed by its full start instant over a span, so two bands at the same time of day on
    /// different days must not collapse into one. A minute-of-day alignment — which is what the
    /// single-day code did — gives all three the same key.
    /// </summary>
    [Fact]
    public async Task StackedBars_GiveEachDaysBandItsOwnStartInstant()
    {
        await SeedMinutesAsync(
            Day1.ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(1).ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(2).ToDateTime(new TimeOnly(12, 0)));

        var windows = await PostAsync("/api/activity-tracking/desktop/stacked-bars", new
        {
            dateFrom = Day1,
            dateTo = Day3,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            windowMinutes = 60
        });

        var starts = windows.EnumerateArray()
            .Select(w => w.GetProperty("windowStart").GetDateTimeOffset())
            .ToList();

        starts.Should().HaveCount(3).And.OnlyHaveUniqueItems(
            "the client keys windows by the full instant over a range, so midday on three different " +
            "days is three windows -- an alignment that only carries the minute of day collides");
        starts.Should().BeEquivalentTo(new[]
        {
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 2, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero)
        });
    }

    /// <summary>
    /// Bands tile each day's window starting at <c>from</c>, and the day's last band is cut off at
    /// <c>to</c> instead of running on into the next day. A 07:00–00:00 window at 480 minutes is
    /// therefore 480 + 480 + 60, and the third band ends at midnight rather than at 07:00 the following
    /// morning — which would double-count the night and put the bar an hour wide of its own label.
    /// </summary>
    [Fact]
    public async Task StackedBars_TruncateTheLastBandOfADayAtTheWindowEdge()
    {
        await SeedMinutesAsync(
            Day1.ToDateTime(new TimeOnly(7, 30)),
            Day1.ToDateTime(new TimeOnly(15, 30)),
            Day1.ToDateTime(new TimeOnly(23, 30)));

        var windows = await PostAsync("/api/activity-tracking/desktop/stacked-bars", new
        {
            dateFrom = Day1,
            dateTo = Day1,
            from = new { hours = 7, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            windowMinutes = 480
        });

        var bounds = windows.EnumerateArray()
            .Select(w => (
                Start: w.GetProperty("windowStart").GetDateTimeOffset(),
                End: w.GetProperty("windowEnd").GetDateTimeOffset()))
            .OrderBy(w => w.Start)
            .ToList();

        bounds.Should().Equal(
            (new DateTimeOffset(2026, 6, 1, 7, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero)),
            (new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 1, 23, 0, 0, TimeSpan.Zero)),
            (new DateTimeOffset(2026, 6, 1, 23, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero)));
    }

    /// <summary>
    /// At a day and over, bands tile the span in whole days instead of tiling inside each day — and
    /// still carry only the time inside the daily window, so a 1440-minute band over a 09:00–17:00
    /// setting holds at most eight hours, not twenty-four.
    /// </summary>
    [Fact]
    public async Task StackedBars_TileTheSpanInWholeDaysAtADayAndOver()
    {
        await SeedMinutesAsync(
            Day1.ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(1).ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(2).ToDateTime(new TimeOnly(12, 0)),
            // On the middle night, so outside every daily window.
            Day1.AddDays(1).ToDateTime(new TimeOnly(3, 30)));

        var windows = await PostAsync("/api/activity-tracking/desktop/stacked-bars", new
        {
            dateFrom = Day1,
            dateTo = Day3,
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 17, minutes = 0 },
            windowMinutes = 1440
        });

        var bands = windows.EnumerateArray().OrderBy(w => w.GetProperty("windowStart").GetDateTimeOffset()).ToList();

        bands.Should().HaveCount(3, "a 1440-minute band is one day of the span, and the span is three days");

        bands[0].GetProperty("windowStart").GetDateTimeOffset().Should()
            .Be(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        bands[0].GetProperty("windowEnd").GetDateTimeOffset().Should()
            .Be(new DateTimeOffset(2026, 6, 1, 17, 0, 0, TimeSpan.Zero),
                "the band is truncated at the daily window's edge, so it names the eight hours it can " +
                "actually contain rather than a full twenty-four");

        bands.Sum(b => b.GetProperty("activities").EnumerateArray()
                .Sum(a => a.GetProperty("totalSeconds").GetInt32()))
            .Should().Be(180, "the row on the middle night is in no band");
    }

    /// <summary>
    /// The card's own number is a span total, so the baseline it is compared against has to be one too.
    /// A seven-day lookback averaging sixty seconds a day, against a three-day span, is a baseline of
    /// one hundred and eighty — comparing the span total to the unscaled per-day mean would report
    /// every ordinary week as up several hundred percent.
    /// </summary>
    [Fact]
    public async Task SummaryCards_ScaleTheBaselineUpToTheLengthOfTheSpan()
    {
        // Current period: three days inside the span.
        await SeedMinutesAsync(
            Day1.ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(1).ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(2).ToDateTime(new TimeOnly(12, 0)));

        // Baseline: the seven days immediately before the span, one minute each, so a mean day of 60s.
        await SeedMinutesAsync(Enumerable.Range(1, 7)
            .Select(d => Day1.AddDays(-d).ToDateTime(new TimeOnly(12, 0)))
            .ToArray());

        var cards = await PostAsync("/api/activity-tracking/desktop/summary-cards", new
        {
            dateFrom = Day1,
            dateTo = Day3,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            topN = 10
        });

        var card = cards.EnumerateArray().Single(c => c.GetProperty("processName").GetString() == "chrome");

        card.GetProperty("isNew").GetBoolean().Should().BeFalse();
        card.GetProperty("active").GetProperty("seconds").GetInt32().Should().Be(180);
        card.GetProperty("active").GetProperty("averageSeconds").GetInt32().Should().Be(180,
            "sixty seconds a day over the seven-day lookback, multiplied out to the three days the " +
            "card's own number covers");
        card.GetProperty("active").GetProperty("percentChange").GetDouble().Should().Be(0.0,
            "a span exactly as busy as its baseline is unchanged, not up 200%");
    }

    /// <summary>
    /// The timeline is a single-day dashboard. It takes the span members for uniformity with the other
    /// three and rejects an actual span, rather than answering with an object whose axis runs to
    /// thousands of ticks and draws every untracked night as tracked-and-idle.
    /// </summary>
    [Fact]
    public async Task Timeline_RejectsASpanRatherThanAnsweringWithSomethingUnreadable()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-tracking/desktop/timeline",
            new
            {
                dateFrom = Day1,
                dateTo = Day3,
                from = new { hours = 0, minutes = 0 },
                to = new { hours = 0, minutes = 0 }
            },
            Json,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The span bound. Without it a single request walks the whole retention window of a partitioned
    /// ledger, and nothing about the request looks unusual.
    /// </summary>
    [Fact]
    public async Task Dashboards_RejectASpanLongerThanAYear()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-tracking/desktop/pie-chart",
            new
            {
                dateFrom = Day1,
                dateTo = Day1.AddDays(400),
                from = new { hours = 0, minutes = 0 },
                to = new { hours = 0, minutes = 0 }
            },
            Json,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The details panel is opened with the outer envelope of the selection, which over a span covers
    /// the nights the daily window excludes. Given that window it counts the same time the slice it was
    /// opened from did.
    /// </summary>
    [Fact]
    public async Task ProcessDetails_ApplyTheDailyWindowToTheEnvelopeItIsOpenedWith()
    {
        await SeedMinutesAsync(
            Day1.ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(1).ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(2).ToDateTime(new TimeOnly(12, 0)),
            Day1.AddDays(1).ToDateTime(new TimeOnly(3, 30)));

        // dateFrom at 09:00 through dateTo at 17:00 — the envelope the pie chart hands over.
        const string envelope = "from=2026-06-01T09:00:00Z&to=2026-06-03T17:00:00Z&processName=chrome";

        var unmasked = await GetAsync($"/api/activity-tracking/desktop/process-details?{envelope}");
        unmasked.GetProperty("totalSeconds").GetInt32().Should().Be(240,
            "without the window the envelope is continuous and picks up the 03:30 row on the middle " +
            "night -- this is the over-count the masking members exist to remove");

        var masked = await GetAsync(
            $"/api/activity-tracking/desktop/process-details?{envelope}&windowStartMinutes=540&windowEndMinutes=1020");
        masked.GetProperty("totalSeconds").GetInt32().Should().Be(180,
            "09:00-17:00 on each of the three days is exactly what the pie slice was measured over");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// One sixty-second desktop row per instant, all for the same process, so every assertion above is
    /// a multiple of sixty and reads as "how many minutes were counted".
    /// </summary>
    private async Task SeedMinutesAsync(params DateTime[] windowStarts)
    {
        await using var db = CreateDbContext();

        foreach (var windowStart in windowStarts)
            db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
            {
                UserId = UserId,
                WindowStart = DateTime.SpecifyKind(windowStart, DateTimeKind.Utc),
                ProcessName = "chrome",
                ProductName = "chrome",
                WindowTitle = "t",
                ExecutablePath = "/usr/bin/chrome",
                IsFullscreen = false,
                ActiveSeconds = 60,
                BackgroundSeconds = 0,
                IsPlayingSound = false,
                ActiveMonitor = 0
            });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<JsonElement> PostAsync(string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }

    private async Task<JsonElement> GetAsync(string route)
    {
        var response = await CreateUserRoleClient().GetAsync(route, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }
}
