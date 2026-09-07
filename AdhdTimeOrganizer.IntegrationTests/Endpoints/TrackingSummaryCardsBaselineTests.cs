using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// What the three per-source summary cards mean by "compared to the last 7 days".
///
/// <para>The baseline block used to be transcribed once per source, and each copy had drifted: none of
/// the three put the lookback through the user's time-of-day window the way it put the span through it,
/// the android copy folded the weekday and the day span on UTC, and <c>allTime</c> reached back to
/// <c>DateTime.MinValue</c>. They now share
/// <see cref="AdhdTimeOrganizer.Tracking.application.endpoint.activityTracking.BaseSummaryCardsEndpoint{TRequest,TItem}"/>,
/// and these cases assert on the numbers the cards print rather than on the routes answering — every one
/// of those bugs returned a perfectly well-formed 200.</para>
///
/// <para>The user is in Bratislava throughout, because two of the three readings are calendar questions
/// and on a UTC host they are all the same question.</para>
///
/// <para>Beside the three baselines sit the two things the card's shape promises: the default card count,
/// which the four routes have to agree on because they are read side by side, and what a card with no
/// baseline says — <c>isNew</c> with both comparison figures null, not a comparison against zero.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingSummaryCardsBaselineTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task SeedAsync(DbContext db)
    {
        var user = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == UserId);
        user.Timezone = Bratislava;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The lookback is read through the same per-day window as the span. The span was always restricted
    /// to the user's 09:00–17:00; the baseline was a bare range predicate over the whole envelope, so it
    /// swept up every night in between — on this window roughly three times too much, which the cards
    /// then report as an ordinary week having collapsed.
    ///
    /// <para>700 seconds inside the window and 7000 outside it, ten times larger so the two readings
    /// cannot be confused: the honest answer is 700 over a nominal seven-day lookback scaled to a
    /// one-day span, i.e. 100. Counting the night as well gives 1100.</para>
    /// </summary>
    [Theory]
    [InlineData("desktop", "processName", "active")]
    [InlineData("web-extension", "domain", "active")]
    [InlineData("android", "appLabel", "stat")]
    public async Task Baseline_IsRestrictedToTheUsersDailyWindow_NotTheWholeEnvelope(
        string source, string keyProperty, string statProperty)
    {
        // The span itself: midday on the user's clock, inside their window.
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), 60);

        // Inside the lookback and inside the window — 12:00 local on the Wednesday.
        await SeedEntryAsync(new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc), 700);

        // Inside the lookback's envelope but in the night the window excludes — midnight local.
        await SeedEntryAsync(new DateTime(2026, 7, 8, 22, 0, 0, DateTimeKind.Utc), 7000);

        var average = await AverageSecondsAsync(source, keyProperty, statProperty, new
        {
            dateFrom = new DateOnly(2026, 7, 14),
            dateTo = new DateOnly(2026, 7, 14),
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 17, minutes = 0 },
            baseline = "Last7Days",
            topN = 10
        });

        average.Should().Be(100,
            "the baseline must be measured through the same 09:00-17:00 window as the span it is " +
            "compared against -- a lookback read over the raw envelope folds in every night the user " +
            "excluded and reports a steady week as a fall of about 90%");
    }

    /// <summary>
    /// The android twin of
    /// <see cref="TrackingDashboardUserZoneTests.SummaryCards_MatchTheSameWeekdayBaselineOnTheUsersCalendar_NotUtcs"/>,
    /// which pinned this rule on the desktop route only — so the android copy drifted out from under a
    /// guard written for it.
    ///
    /// <para>The requested Tuesday begins at 22:00Z on Monday, so a target weekday read off the raw
    /// instant is Monday and <c>steady</c>'s baseline row — midday on the previous Tuesday — is thrown
    /// out of its own baseline. <c>midnight</c> covers the row side of the same reading: half past
    /// midnight on the user's Tuesday is still Monday in UTC.</para>
    /// </summary>
    [Fact]
    public async Task AndroidBaseline_MatchesTheSameWeekdayOnTheUsersCalendar_NotUtcs()
    {
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), 60, "steady");
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), 60, "midnight");

        // 2026-07-07 12:00 local — a Tuesday in UTC as well.
        await SeedEntryAsync(new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc), 600, "steady");
        // 2026-07-07 00:30 local — still Monday the 6th in UTC.
        await SeedEntryAsync(new DateTime(2026, 7, 6, 22, 30, 0, DateTimeKind.Utc), 600, "midnight");

        var cards = await PostAsync("/api/activity-tracking/android/summary-cards", new
        {
            dateFrom = new DateOnly(2026, 7, 14),
            dateTo = new DateOnly(2026, 7, 14),
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            baseline = "SameWeekday",
            topN = 10
        });

        var isNewByApp = cards.EnumerateArray()
            .ToDictionary(c => c.GetProperty("appLabel").GetString()!, c => c.GetProperty("isNew").GetBoolean());

        isNewByApp.Should().ContainKey("steady");
        isNewByApp["steady"].Should().BeFalse(
            "the requested Tuesday starts at 22:00Z on Monday, so reading the target weekday off the raw " +
            "instant makes it Monday and discards a baseline session from midday on the previous Tuesday");

        isNewByApp.Should().ContainKey("midnight");
        isNewByApp["midnight"].Should().BeFalse(
            "half past midnight on the user's Tuesday is still Monday in UTC, so the per-session weekday " +
            "must be read through the zone too");
    }

    /// <summary>
    /// <c>allTime</c> is floored a year and a day back rather than at the beginning of time. Two things
    /// hang off that: the read stays bounded (an unfloored one is the heaviest query in the app), and the
    /// lower bound is an instant resolved through the user's zone instead of an
    /// <c>Unspecified</c>-kind <c>DateTime.MinValue</c>, which Npgsql refuses to write to a
    /// <c>timestamptz</c> — a 500 on every <c>allTime</c> card in production.
    ///
    /// <para><b>The 500 half cannot be reproduced here</b>: <c>PostgresContainerFixture</c> sets
    /// <c>Npgsql.EnableLegacyTimestampBehavior</c>, under which <c>Unspecified</c> means UTC, so this
    /// suite runs a different date-time contract than <c>Program.cs</c> does. What the numbers below do
    /// catch is the floor itself, which is the same fix: an 18-month-old row must not reach the divisor.
    /// 300 seconds on a single in-floor day scaled to a one-day span is 300; dragging the 2025 row in
    /// stretches the divisor to 555 days and the card reads 180.</para>
    ///
    /// <para>Parameterised over all three sources because the <c>MinValue</c> floor was transcribed into
    /// each of them separately — and a route that 500s in production is a route this suite can only see
    /// answering, so the number is what has to be asserted on all three rather than the status.</para>
    /// </summary>
    [Theory]
    [InlineData("desktop", "processName", "active")]
    [InlineData("web-extension", "domain", "active")]
    [InlineData("android", "appLabel", "stat")]
    public async Task AllTimeBaseline_IsFlooredAtTheMaximumSpan_NotAtTheBeginningOfTime(
        string source, string keyProperty, string statProperty)
    {
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), 60);

        // Inside the 366-day floor (which lands on 2025-07-13).
        await SeedEntryAsync(new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc), 300);

        // Well outside it — large enough that including it cannot be mistaken for rounding.
        await SeedEntryAsync(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc), 100_000);

        var average = await AverageSecondsAsync(source, keyProperty, statProperty, new
        {
            dateFrom = new DateOnly(2026, 7, 14),
            dateTo = new DateOnly(2026, 7, 14),
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            baseline = "AllTime",
            topN = 10
        });

        average.Should().Be(300,
            "allTime reaches back a year and a day, not to DateTime.MinValue -- a row from January 2025 " +
            "is outside the floor, and letting it in stretches the per-day divisor to 555 days");
    }

    /// <summary>
    /// The strongest form available for the windowed lookback: <c>summary-cards</c> and
    /// <c>focus-metrics</c> sit on the same screen and <c>domain-map.md:138-140</c> says they must agree
    /// about what "compared to the last 7 days" measures. Only <c>focus-metrics</c> ever did — it built a
    /// <see cref="AdhdTimeOrganizer.Tracking.domain.helper.DailyWindowSet"/> for its lookback while the
    /// cards used a bare envelope predicate — so the two surfaces printed different weeks.
    ///
    /// <para>The lookback is shaped so the two figures are the same number by construction: each of the
    /// seven days holds one unbroken ten-minute run inside the 09:00–17:00 window, so the seconds the
    /// cards attribute to the mean day <b>are</b> the block <c>focus-metrics</c> reports for it, 600
    /// either way. Six of those days also hold a hundred minutes at 22:00 local — inside the lookback's
    /// outer envelope, inside no day's window. Let that in on the cards' side and the average becomes
    /// 5742; let it in on the metrics' side and the mean longest block becomes 5228.6. The two only meet
    /// at 600.</para>
    /// </summary>
    [Fact]
    public async Task Baseline_MeasuresTheSameWindowedLookbackThatFocusMetricsDoes()
    {
        // The span: one unbroken ten-minute run at midday on the user's clock.
        await SeedDesktopRunAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), 10);

        foreach (var day in Enumerable.Range(7, 7))
        {
            // 12:00 local on each of the seven lookback days — inside the window.
            await SeedDesktopRunAsync(new DateTime(2026, 7, day, 10, 0, 0, DateTimeKind.Utc), 10);

            // 22:00 local on every night between two of them — inside the envelope, inside no window.
            // Ten times the size, so a reading that folds it in cannot be mistaken for rounding.
            if (day < 13)
                await SeedDesktopRunAsync(new DateTime(2026, 7, day, 20, 0, 0, DateTimeKind.Utc), 100);
        }

        var card = await AverageSecondsAsync("desktop", "processName", "active", new
        {
            dateFrom = new DateOnly(2026, 7, 14),
            dateTo = new DateOnly(2026, 7, 14),
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 17, minutes = 0 },
            baseline = "Last7Days",
            topN = 10
        });

        var metrics = await PostAsync("/api/activity-tracking/desktop/focus-metrics", new
        {
            dateFrom = new DateOnly(2026, 7, 14),
            dateTo = new DateOnly(2026, 7, 14),
            from = new { hours = 9, minutes = 0 },
            to = new { hours = 17, minutes = 0 },
            focusGapSeconds = 120,
            baseline = "Last7Days"
        });

        var block = metrics.GetProperty("baseline").GetProperty("longestBlockSeconds").GetDouble();

        card.Should().Be(600,
            "seven days of a single ten-minute run inside the window is 600 seconds on the mean day, " +
            "and the span is one day -- counting the nights as well gives 5742 and reports a steady " +
            "week as a collapse");
        block.Should().Be(600,
            "the same seven days, read the same way, through the window set focus-metrics has always " +
            "built for its lookback");
        ((double)card).Should().Be(block,
            "the two dashboards answer 'compared to the last 7 days' on one screen, and a lookback " +
            "windowed on one side and not the other makes them disagree about the same week");
    }

    /// <summary>
    /// How many cards a route returns when the request does not ask. All four read
    /// <see cref="AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.SummaryCardsRequest.DefaultTopN"/>;
    /// android used to default to four of its own, so the same screen showed five desktop apps beside
    /// four android ones and the difference read as a design decision rather than a transcription.
    /// </summary>
    [Theory]
    [InlineData("desktop", "processName")]
    [InlineData("web-extension", "domain")]
    [InlineData("android", "appLabel")]
    public async Task TopN_DefaultsToFiveOnEverySource(string source, string keyProperty)
    {
        // Seven items, descending, so the cut is unambiguous and its position is visible in the keys.
        foreach (var rank in Enumerable.Range(1, 7))
            await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), (8 - rank) * 100, $"item{rank}");

        var cards = await PostAsync($"/api/activity-tracking/{source}/summary-cards", new
        {
            dateFrom = new DateOnly(2026, 7, 14),
            dateTo = new DateOnly(2026, 7, 14),
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            baseline = "Last7Days"
        });

        cards.EnumerateArray().Select(c => c.GetProperty(keyProperty).GetString()).Should()
            .Equal(new[] { "item1", "item2", "item3", "item4", "item5" },
                "an omitted topN is five cards, the busiest first -- one number for all four routes " +
                "because they are read side by side");
    }

    /// <summary>
    /// An item the lookback never saw is <c>isNew</c>, and both comparison figures are <c>null</c> rather
    /// than zero: the client renders a card with no comparison on it, where a zero average would render
    /// as "typically none, now up 100%" on every item of a new user's first day.
    ///
    /// <para>The item that <i>does</i> have a baseline is asserted alongside, because "every card is new"
    /// is the failure mode a dropped baseline row produces and it passes an <c>isNew</c> assertion made
    /// on its own.</para>
    /// </summary>
    [Theory]
    [InlineData("desktop", "processName", "active")]
    [InlineData("web-extension", "domain", "active")]
    [InlineData("android", "appLabel", "stat")]
    public async Task AnItemWithNoBaseline_IsNewAndCarriesNoComparison(
        string source, string keyProperty, string statProperty)
    {
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), 60);
        await SeedEntryAsync(new DateTime(2026, 7, 14, 10, 0, 0, DateTimeKind.Utc), 60, "newcomer");

        // Only "chrome" ran during the lookback.
        await SeedEntryAsync(new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc), 700);

        var cards = await PostAsync($"/api/activity-tracking/{source}/summary-cards", new
        {
            dateFrom = new DateOnly(2026, 7, 14),
            dateTo = new DateOnly(2026, 7, 14),
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            baseline = "Last7Days",
            topN = 10
        });

        var byKey = cards.EnumerateArray().ToDictionary(c => c.GetProperty(keyProperty).GetString()!);

        byKey.Should().ContainKey("newcomer");
        byKey["newcomer"].GetProperty("isNew").GetBoolean().Should().BeTrue(
            "nothing under that key is anywhere in the lookback");

        var stat = byKey["newcomer"].GetProperty(statProperty);
        stat.GetProperty("averageSeconds").ValueKind.Should().Be(JsonValueKind.Null,
            "there is no average of an empty lookback -- a zero here renders as 'typically none'");
        stat.GetProperty("percentChange").ValueKind.Should().Be(JsonValueKind.Null,
            "and no ratio against it, which a zero average would turn into a permanent +100%");

        byKey.Should().ContainKey("chrome");
        byKey["chrome"].GetProperty("isNew").GetBoolean().Should().BeFalse(
            "chrome ran four days earlier, so the flag has to discriminate rather than being true for " +
            "every card -- which is what a dropped baseline read looks like");
    }

    // ---- helpers ---------------------------------------------------------

    private async Task<long> AverageSecondsAsync(
        string source, string keyProperty, string statProperty, object body)
    {
        var cards = await PostAsync($"/api/activity-tracking/{source}/summary-cards", body);

        var card = cards.EnumerateArray()
            .Single(c => c.GetProperty(keyProperty).GetString() == "chrome");

        return card.GetProperty(statProperty).GetProperty("averageSeconds").GetInt64();
    }

    /// <summary>
    /// Seeds the same instant and the same number of seconds into all three ledgers, so one seeding path
    /// serves the theory. The key is the process name, the domain and the app label alike — the three
    /// cards group on different columns and the assertions look one item up by name.
    /// </summary>
    private async Task SeedEntryAsync(DateTime instantUtc, int seconds, string name = "chrome")
    {
        await using var db = CreateDbContext();

        db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
        {
            UserId = UserId,
            WindowStart = instantUtc,
            ProcessName = name,
            ProductName = name,
            WindowTitle = "t",
            ExecutablePath = $"/usr/bin/{name}",
            IsFullscreen = false,
            ActiveSeconds = seconds,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        });

        db.Set<WebExtensionActivityEntry>().Add(new WebExtensionActivityEntry
        {
            UserId = UserId,
            WindowStart = instantUtc,
            Domain = name,
            Url = null,
            ActiveSeconds = seconds,
            BackgroundSeconds = 0,
            IsFinal = true
        });

        db.Set<AndroidSessionData>().Add(new AndroidSessionData
        {
            UserId = UserId,
            DeviceId = "device",
            PackageName = $"com.example.{name}",
            AppLabel = name,
            SessionStartUtc = instantUtc,
            SessionEndUtc = instantUtc.AddSeconds(seconds),
            DurationSeconds = seconds
        });

        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>
    /// <paramref name="minutes"/> consecutive one-minute desktop rows of sixty active seconds each.
    /// <c>summary-cards</c> only sums them, but <c>focus-metrics</c> stitches them into sessions, so the
    /// cross-surface case has to seed a run rather than one row carrying the whole duration — a lone row
    /// is a sixty-second session there however many seconds it claims.
    /// </summary>
    private async Task SeedDesktopRunAsync(DateTime startUtc, int minutes, string name = "chrome")
    {
        await using var db = CreateDbContext();

        for (var i = 0; i < minutes; i++)
            db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
            {
                UserId = UserId,
                WindowStart = startUtc.AddMinutes(i),
                ProcessName = name,
                ProductName = name,
                WindowTitle = "t",
                ExecutablePath = $"/usr/bin/{name}",
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
}
