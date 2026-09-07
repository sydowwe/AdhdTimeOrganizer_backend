using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the overlap rule of the unified dashboard — the one mechanism in this slice whose failure mode
/// is a perfectly well-formed 200 with a wrong number on it.
///
/// <para>Every assertion here is on seconds, never on a route answering. Get the rule wrong and the
/// page still renders: the merged day is simply shorter or longer than the day the user lived, and the
/// three figures that let a user check it — <c>countedSeconds</c>, <c>displacedSeconds</c> and
/// <c>displacedTo</c> — agree with each other while all being wrong together.</para>
///
/// <para>The user's zone is the fixture default, UTC, so the instants below read the same on both
/// sides.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingUnifiedDashboardTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Day = new(2026, 6, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly string[] AllSources = ["webExtension", "desktop", "android"];

    private const string PieChartRoute = "/api/activity-tracking/unified/pie-chart";
    private const string SummaryCardsRoute = "/api/activity-tracking/unified/summary-cards";
    private const string StackedBarsRoute = "/api/activity-tracking/unified/stacked-bars";

    /// <summary>
    /// The substantive half of the rule. One minute in a browser is one <c>chrome.exe</c> row to the
    /// desktop agent and one <c>github.com</c> row to the extension; the extension wins, the desktop is
    /// told exactly what it lost and to whom, and the merged day is sixty seconds long rather than a
    /// hundred and twenty.
    /// </summary>
    [Fact]
    public async Task Sources_CreditOverlappingTimeToOneTrackerAndTellTheOtherWhoTookIt()
    {
        await SeedDesktopAsync(("chrome", "Google Chrome", At(12, 0), 60, 0));
        await SeedWebExtensionAsync(("github.com", At(12, 0), 60, 0));

        var sources = await PostAsync("/api/activity-tracking/unified/sources", Request(AllSources));

        var web = SourceRow(sources, "webExtension");
        var desktop = SourceRow(sources, "desktop");

        web.GetProperty("countedSeconds").GetInt32().Should().Be(60);
        web.GetProperty("displacedSeconds").GetInt32().Should().Be(0);

        desktop.GetProperty("countedSeconds").GetInt32().Should().Be(0,
            "the extension outranks the desktop agent for the same minute of wall clock");
        desktop.GetProperty("displacedSeconds").GetInt32().Should().Be(60);
        desktop.GetProperty("displacedTo").GetString().Should().Be("webExtension");

        var pie = await PostAsync("/api/activity-tracking/unified/pie-chart", Request(AllSources));

        pie.GetProperty("totals").GetProperty("totalSeconds").GetInt32().Should().Be(60,
            "the parts add up: the merged total is the sum of what each source was credited with, " +
            "not the sum of what the three ledgers recorded");
    }

    /// <summary>
    /// Losing is partial. A desktop run that loses one of its three minutes to the extension keeps the
    /// other two — the tempting shortcut, suppressing the browser process wholesale while the extension
    /// is selected, leaves an hour the user spent in a browser showing no browser at all.
    /// </summary>
    [Fact]
    public async Task PieChart_SplitsAPartiallyOverlappedRunInsteadOfDroppingIt()
    {
        await SeedDesktopAsync(
            ("chrome", "Google Chrome", At(12, 0), 60, 0),
            ("chrome", "Google Chrome", At(12, 1), 60, 0),
            ("chrome", "Google Chrome", At(12, 2), 60, 0));

        // The extension only saw the middle minute -- the other two are a PDF viewer, a chrome:// page,
        // or simply a window open before the extension started.
        await SeedWebExtensionAsync(("github.com", At(12, 1), 60, 0));

        var pie = await PostAsync("/api/activity-tracking/unified/pie-chart", Request(AllSources));

        var items = pie.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("label").GetString()!, i => i.GetProperty("totalSeconds").GetInt32());

        items["Google Chrome"].Should().Be(120,
            "browser time the extension could not see is genuine desktop time and must survive");
        items["github.com"].Should().Be(60);
        pie.GetProperty("totals").GetProperty("totalSeconds").GetInt32().Should().Be(180);

        var sources = await PostAsync("/api/activity-tracking/unified/sources", Request(AllSources));
        var desktop = SourceRow(sources, "desktop");

        desktop.GetProperty("countedSeconds").GetInt32().Should().Be(120);
        desktop.GetProperty("displacedSeconds").GetInt32().Should().Be(60,
            "counted plus displaced is what the desktop dashboard reports for the same span -- three " +
            "minutes -- which is the arithmetic the page invites the user to check");
    }

    /// <summary>
    /// Level 1 of the rule, and the case that makes the two levels worth ordering. A browser left open
    /// on a second monitor while the user is on their phone is desktop <i>background</i> against android
    /// <i>foreground</i>. Rank alone would credit the desktop — which outranks android — and quietly
    /// delete the phone time.
    /// </summary>
    [Fact]
    public async Task Sources_LetForegroundBeatBackgroundEvenAgainstAHigherRankedTracker()
    {
        await SeedDesktopAsync(("chrome", "Google Chrome", At(12, 0), 0, 60));
        await SeedAndroidAsync(("com.slack", "Slack", At(12, 0), At(12, 1), 60));

        var sources = await PostAsync("/api/activity-tracking/unified/sources", Request(AllSources));

        SourceRow(sources, "android").GetProperty("countedSeconds").GetInt32().Should().Be(60,
            "the phone was in the user's hands; the desktop only had a window open");
        SourceRow(sources, "desktop").GetProperty("countedSeconds").GetInt32().Should().Be(0);
        SourceRow(sources, "desktop").GetProperty("displacedTo").GetString().Should().Be("android");
    }

    /// <summary>
    /// A deselected source takes no part: it contributes nothing and, crucially, displaces nothing. Turn
    /// the extension off and the browser hour comes <b>back</b> to the desktop agent as
    /// <c>Google Chrome</c> — it does not vanish, and it does not stay credited to a source that is no
    /// longer on screen. This is the whole reason <c>sources</c> is a request field.
    /// </summary>
    [Fact]
    public async Task Sources_GiveTheTimeBackWhenTheWinningTrackerIsDeselected()
    {
        await SeedDesktopAsync(("chrome", "Google Chrome", At(12, 0), 60, 0));
        await SeedWebExtensionAsync(("github.com", At(12, 0), 60, 0));

        var sources = await PostAsync("/api/activity-tracking/unified/sources", Request(["desktop"]));

        var desktop = SourceRow(sources, "desktop");
        desktop.GetProperty("countedSeconds").GetInt32().Should().Be(60,
            "with the extension deselected nothing displaces the desktop agent");
        desktop.GetProperty("displacedSeconds").GetInt32().Should().Be(0);

        var web = SourceRow(sources, "webExtension");
        web.GetProperty("countedSeconds").GetInt32().Should().Be(0);
        web.GetProperty("displacedSeconds").GetInt32().Should().Be(0);
        web.GetProperty("hasData").GetBoolean().Should().BeTrue(
            "hasData is independent of selection -- it is how the filter tells the user there is " +
            "browsing data they are not looking at");
    }

    /// <summary>
    /// One application, one label, one colour. <c>slack.exe</c> ships under the product name
    /// <c>Slack</c> and <c>com.slack</c> under the app label <c>slack</c>; unjoined they would arrive on
    /// one page under one name in two colours, and the pie legend, the bars and the timeline swatches
    /// would each disagree with the others.
    /// </summary>
    [Fact]
    public async Task PieChart_JoinsOneApplicationSeenByTwoTrackersIntoOneItem()
    {
        await SeedDesktopAsync(("slack", "Slack", At(12, 0), 60, 0));
        await SeedAndroidAsync(("com.slack", "slack", At(13, 0), At(13, 1), 60));

        var pie = await PostAsync("/api/activity-tracking/unified/pie-chart", Request(AllSources));

        var items = pie.GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCount(1, "one application is one item however many trackers saw it");
        items[0].GetProperty("label").GetString().Should().Be("Slack",
            "the spelling comes from the highest-precedence source that saw the item, so it is stable " +
            "however the ledgers happen to be read");
        items[0].GetProperty("totalSeconds").GetInt32().Should().Be(120);
        items[0].GetProperty("sources").EnumerateArray().Select(s => s.GetString())
            .Should().BeEquivalentTo("desktop", "android");
    }

    /// <summary>
    /// The two definitions only the merged view has to settle. Moving from Slack on the desktop to Slack
    /// on the phone is a device change, not a change of what is being attended to — counting it would
    /// make the merged switch count read as worse attention than the reality — while moving from Slack
    /// to an editor is a switch whichever machine each side was on.
    /// </summary>
    [Fact]
    public async Task FocusMetrics_CountADeviceChangeOnOneItemAsNoSwitch()
    {
        await SeedDesktopAsync(
            ("slack", "Slack", At(10, 0), 60, 0),
            ("slack", "Slack", At(10, 1), 60, 0),
            ("slack", "Slack", At(10, 2), 60, 0));

        // Straight on from the phone, same application, no gap.
        await SeedAndroidAsync(("com.slack", "Slack", At(10, 3), At(10, 6), 180));

        await SeedDesktopAsync(
            ("code", "Code", At(10, 6), 60, 0),
            ("code", "Code", At(10, 7), 60, 0),
            ("code", "Code", At(10, 8), 60, 0));

        var metrics = await PostAsync("/api/activity-tracking/unified/focus-metrics", new
        {
            dateFrom = Day,
            dateTo = Day,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            sources = AllSources,
            focusGapSeconds = 120
        });

        metrics.GetProperty("switchCount").GetInt32().Should().Be(1,
            "Slack-desktop to Slack-phone is a device change and not a switch; Slack to the editor is " +
            "the one switch in the stream");
        metrics.GetProperty("sessionCount").GetInt32().Should().Be(2,
            "the run on one label continues across the device change rather than starting again");
    }

    /// <summary>
    /// The merged timeline is read top to bottom as one day. Two lanes claiming the same minute would
    /// make it three transparencies laid over each other instead, which is the thing it exists to stop
    /// being.
    /// </summary>
    [Fact]
    public async Task Timeline_NeverPutsOneMinuteInTwoLanes()
    {
        await SeedDesktopAsync(
            ("chrome", "Google Chrome", At(12, 0), 60, 0),
            ("chrome", "Google Chrome", At(12, 1), 60, 0));
        await SeedWebExtensionAsync(
            ("github.com", At(12, 0), 60, 0),
            ("github.com", At(12, 1), 60, 0));

        var timeline = await PostAsync("/api/activity-tracking/unified/timeline", Request(AllSources));

        var spans = new[] { "webExtensionSessions", "desktopSessions", "androidSessions" }
            .SelectMany(lane => timeline.GetProperty(lane).EnumerateArray().Select(session => (
                Lane: lane,
                Start: session.GetProperty("startedAt").GetDateTimeOffset(),
                End: session.GetProperty("endedAt").GetDateTimeOffset())))
            .ToList();

        foreach (var left in spans)
        foreach (var right in spans.Where(other => other.Lane != left.Lane))
            (left.Start < right.End && right.Start < left.End).Should().BeFalse(
                "no two sessions in different lanes may overlap in wall-clock time");

        timeline.GetProperty("webExtensionSessions").EnumerateArray().Should().HaveCount(1);
        timeline.GetProperty("desktopSessions").EnumerateArray().Should().BeEmpty(
            "the extension took both minutes outright, so the desktop lane has nothing left to draw");
    }

    /// <summary>
    /// Ids are unique across the whole response rather than merely within a lane. Nothing in the client
    /// breaks on a collision today, but a duplicate id would silently drop a session the day the three
    /// lanes are ever rendered from one loop.
    /// </summary>
    [Fact]
    public async Task Timeline_GivesEverySessionAnIdUniqueAcrossAllThreeLanes()
    {
        await SeedDesktopAsync(
            ("code", "Code", At(9, 0), 60, 0),
            ("code", "Code", At(9, 1), 60, 0));
        await SeedWebExtensionAsync(
            ("github.com", At(11, 0), 60, 0),
            ("github.com", At(11, 1), 60, 0));
        await SeedAndroidAsync(("com.slack", "Slack", At(13, 0), At(13, 5), 300));

        var timeline = await PostAsync("/api/activity-tracking/unified/timeline", Request(AllSources));

        var ids = new[] { "webExtensionSessions", "desktopSessions", "androidSessions" }
            .SelectMany(lane => timeline.GetProperty(lane).EnumerateArray()
                .Select(session => session.GetProperty("id").GetInt32()))
            .ToList();

        ids.Should().HaveCount(3).And.OnlyHaveUniqueItems();
    }

    /// <summary>
    /// <c>totals</c> is read unconditionally by the client, so an empty span has to answer with an empty
    /// breakdown and a zeroed total rather than with a missing object.
    /// </summary>
    [Fact]
    public async Task PieChart_ReturnsTotalsEvenWhenThereIsNothingToShow()
    {
        var pie = await PostAsync("/api/activity-tracking/unified/pie-chart", Request(AllSources));

        pie.GetProperty("items").EnumerateArray().Should().BeEmpty();
        pie.GetProperty("totals").GetProperty("totalSeconds").GetInt32().Should().Be(0);
        pie.GetProperty("totals").GetProperty("totalItems").GetInt32().Should().Be(0);
        pie.GetProperty("totals").GetProperty("totalSessions").GetInt32().Should().Be(0);
    }

    /// <summary>
    /// An empty selection asks for a picture of nothing. Defaulting it to all three would answer a
    /// question the client never asks — its filter refuses to turn off the last source — and hide the
    /// bug that produced it.
    /// </summary>
    [Theory]
    [InlineData(PieChartRoute)]
    [InlineData(SummaryCardsRoute)]
    [InlineData(StackedBarsRoute)]
    public async Task Dashboards_RejectAnEmptySourceSelection(string route) =>
        await AssertRejectedAsync(route, []);

    /// <summary>An unknown member is a typo in a shared link, and should fail rather than quietly narrow the picture.</summary>
    [Theory]
    [InlineData(PieChartRoute)]
    [InlineData(SummaryCardsRoute)]
    [InlineData(StackedBarsRoute)]
    public async Task Dashboards_RejectAnUnknownSource(string route) =>
        await AssertRejectedAsync(route, ["desktop", "wristwatch"]);

    /// <summary>
    /// Only the timeline is single-day. These two answer over a span, and a span request that 400s
    /// would leave the merged page blank on every range the client offers but "today".
    /// </summary>
    [Theory]
    [InlineData(SummaryCardsRoute)]
    [InlineData(StackedBarsRoute)]
    public async Task Dashboards_AcceptASpan(string route)
    {
        await SeedDesktopAsync(("chrome", "Chrome", At(12, 0), 60, 0));

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            route,
            new
            {
                dateFrom = Day,
                dateTo = Day.AddDays(2),
                from = new { hours = 0, minutes = 0 },
                to = new { hours = 0, minutes = 0 },
                sources = AllSources,
                // Ignored by summary-cards, required by stacked-bars.
                windowMinutes = 1440
            },
            Json,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task AssertRejectedAsync(string route, string[] sources)
    {
        var body = route == StackedBarsRoute ? BarsRequest(sources, 60) : Request(sources);

        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The merged timeline is a single-day dashboard for the same reason the three per-source ones are,
    /// only more so: a merged month of sessions is even less legible than one source's.
    /// </summary>
    [Fact]
    public async Task Timeline_RejectsASpan()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-tracking/unified/timeline",
            new
            {
                dateFrom = Day,
                dateTo = Day.AddDays(2),
                from = new { hours = 0, minutes = 0 },
                to = new { hours = 0, minutes = 0 },
                sources = AllSources
            },
            Json,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- stacked bars ----------------------------------------------------

    /// <summary>
    /// The identity the bars exist to hold. A band allocates its segments out of <b>one</b> pool, so
    /// what the columns add up to is what the band is tall — and because both surfaces read the same
    /// rounded ledger, that height is also what the same span contributes to the pie.
    ///
    /// <para>The seed is deliberately in the fractional regime: the browser holds 35 seconds of the
    /// minute and the desktop agent's two processes share the 25 left over, twelve and a half seconds
    /// each. Round each segment on its own and the parts stop adding up — visibly, on exactly the busy
    /// days a user would bother to check.</para>
    /// </summary>
    [Fact]
    public async Task StackedBars_SumOfABandsSegmentsEqualsTheBandsOwnHeight()
    {
        await SeedFractionalOverlapAsync();

        var pie = await PostAsync(PieChartRoute, Request(AllSources));
        var bands = await PostAsync(StackedBarsRoute, BarsRequest(AllSources, 1440));

        var pieItems = pie.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("label").GetString()!, i => i.GetProperty("totalSeconds").GetInt32());

        pieItems["github.com"].Should().Be(35);
        pieItems["Chrome"].Should().Be(13);
        pieItems["Code"].Should().Be(12,
            "twelve and a half seconds each cannot both be thirteen: the leftover second goes to one of " +
            "them by the allocator's tie-break, not to both");

        var band = bands.EnumerateArray().ToList().Should().ContainSingle(
            "a 1440-minute band over a one-day span is the whole day").Subject;

        band.GetProperty("items").EnumerateArray()
            .Sum(item => item.GetProperty("activeSeconds").GetInt32() +
                         item.GetProperty("backgroundSeconds").GetInt32())
            .Should().Be(pie.GetProperty("totals").GetProperty("totalSeconds").GetInt32(),
                "the band and the pie are two projections of one rounded ledger over the same span, so " +
                "they cannot disagree about a second unless one of them rounded a second time");

        pie.GetProperty("totals").GetProperty("totalSeconds").GetInt32().Should().Be(60,
            "one minute of wall clock is sixty seconds however many trackers saw it");
    }

    /// <summary>
    /// One application is one segment. Unjoined, <c>slack.exe</c> and <c>com.slack</c> would stack twice
    /// in the same column in the same colour, and a bar has no room to explain why — the source
    /// dimension is carried by <c>sources</c>, which names both trackers on the one item.
    /// </summary>
    [Fact]
    public async Task StackedBars_MergeItemsByLabelAcrossSources()
    {
        await SeedDesktopAsync(("slack", "Slack", At(12, 0), 60, 0));
        await SeedAndroidAsync(("com.slack", "slack", At(13, 0), At(13, 1), 60));

        var bands = await PostAsync(StackedBarsRoute, BarsRequest(AllSources, 1440));

        var items = bands.EnumerateArray().Single().GetProperty("items").EnumerateArray().ToList();

        items.Should().HaveCount(1, "one application is one item however many trackers saw it");
        items[0].GetProperty("label").GetString().Should().Be("Slack",
            "the spelling comes from the highest-precedence source that saw the item");
        items[0].GetProperty("activeSeconds").GetInt32().Should().Be(120);
        items[0].GetProperty("sources").EnumerateArray().Select(s => s.GetString())
            .Should().BeEquivalentTo("desktop", "android");
    }

    /// <summary>
    /// The same client component draws the merged bars and the per-source ones, so the axis has to be
    /// the same axis. With only the desktop agent selected the two responses are the same day tiled the
    /// same way, and a band boundary that drifted would misalign two charts sitting on one screen.
    /// </summary>
    [Fact]
    public async Task StackedBars_UseTheSameBandsAsThePerSourceDashboards()
    {
        await SeedDesktopAsync(
            ("code", "Code", At(9, 0), 60, 0),
            ("code", "Code", At(11, 30), 60, 0),
            ("chrome", "Chrome", At(13, 0), 60, 0));

        var unified = await PostAsync(StackedBarsRoute, BarsRequest(["desktop"], 60));
        var desktop = await PostAsync("/api/activity-tracking/desktop/stacked-bars", new
        {
            dateFrom = Day,
            dateTo = Day,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            windowMinutes = 60
        });

        Bands(unified).Should().HaveCount(3).And.Equal(Bands(desktop));
    }

    /// <summary>
    /// A band with nothing in it is absent rather than zero-filled. The client generates its own empty
    /// slots on the same alignment, and over a span most of the slots are empty — sending them would be
    /// a payload made almost entirely of zeroes.
    /// </summary>
    [Fact]
    public async Task StackedBars_OmitEmptyBands()
    {
        await SeedDesktopAsync(("code", "Code", At(12, 0), 60, 0));

        var bands = (await PostAsync(StackedBarsRoute, BarsRequest(AllSources, 60)))
            .EnumerateArray().ToList();

        bands.Should().HaveCount(1,
            "a full day at sixty minutes is twenty-four slots, and twenty-three of them have no activity");
        bands[0].GetProperty("windowStart").GetDateTimeOffset()
            .Should().Be(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// The accepted widths are the set in <c>DashboardDateRangeRules.AllowedWindowMinutes</c>. Anything
    /// else is a client that has invented a bucket width, and answering it would tile the day on an
    /// alignment the per-source bars beside it do not share.
    /// </summary>
    [Fact]
    public async Task StackedBars_RejectAnUnknownWindowMinutes()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            StackedBarsRoute, BarsRequest(AllSources, 45), Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- summary cards ---------------------------------------------------

    /// <summary>
    /// Two surfaces, one ledger. The cards and the pie are the same merged day grouped the same way, so
    /// an item's card total and its slice are the same number — they can only diverge if one of them
    /// recomputes from the fractional merge instead of reading the rounded entries.
    /// </summary>
    [Fact]
    public async Task SummaryCards_ReturnTheTopItemsOfTheMergedDay()
    {
        await SeedFractionalOverlapAsync();

        var pie = await PostAsync(PieChartRoute, Request(AllSources));
        var cards = await PostAsync(SummaryCardsRoute, Request(AllSources));

        var pieItems = pie.GetProperty("items").EnumerateArray()
            .ToDictionary(i => i.GetProperty("label").GetString()!, i => i.GetProperty("totalSeconds").GetInt32());

        cards.EnumerateArray()
            .ToDictionary(c => c.GetProperty("label").GetString()!, c => c.GetProperty("totalSeconds").GetInt32())
            .Should().Equal(pieItems,
                "three items is inside the default topN, so the two surfaces are showing the same day");
    }

    /// <summary>
    /// The lookback is merged exactly as the span is. Seven days on which the browser and the desktop
    /// agent both recorded the same minute are seven merged minutes, not fourteen — compare a
    /// de-overlapped span against a double-counted baseline and every user's first merged week reads as
    /// a fifty percent collapse in usage that never happened.
    /// </summary>
    [Fact]
    public async Task SummaryCards_MergeTheLookbackTheSameWayAsTheSpan()
    {
        // The span day and the seven lookback days behind it, each one minute seen by both trackers.
        var days = Enumerable.Range(0, 8).Select(i => Day.AddDays(-i)).ToArray();

        await SeedDesktopAsync(days.Select(day => ("chrome", "Chrome", On(day, 12, 0), 60, 0)).ToArray());
        await SeedWebExtensionAsync(days.Select(day => ("github.com", On(day, 12, 0), 60, 0)).ToArray());

        var card = (await PostAsync(SummaryCardsRoute, Request(AllSources))).EnumerateArray().Single();

        card.GetProperty("label").GetString().Should().Be("github.com",
            "the extension outranks the desktop agent for the same minute on every one of the days");
        card.GetProperty("totalSeconds").GetInt32().Should().Be(60);

        card.GetProperty("active").GetProperty("averageSeconds").GetInt32().Should().Be(60,
            "sixty merged seconds a day over the seven-day lookback -- not the hundred and twenty the " +
            "two ledgers add up to before the overlap rule is applied to them");
        card.GetProperty("active").GetProperty("percentChange").GetDouble().Should().Be(0.0,
            "a day exactly as busy as its baseline is unchanged; anything else is the merge itself " +
            "being reported as a change in behaviour");
    }

    /// <summary>
    /// The card's own number is a span total, so the baseline it is compared against has to be one too:
    /// the mean day of the lookback multiplied out to the length of the span. This is the merged
    /// counterpart of <c>TrackingDashboardDateRangeTests.SummaryCards_ScaleTheBaselineUpToTheLengthOfTheSpan</c>.
    /// </summary>
    [Fact]
    public async Task SummaryCards_ScaleTheMeanDayToTheLengthOfTheSpan()
    {
        // Five days inside the span, and the seven immediately before it as the lookback: a mean day of
        // sixty seconds either way.
        var days = Enumerable.Range(0, 5).Select(i => Day.AddDays(i))
            .Concat(Enumerable.Range(1, 7).Select(i => Day.AddDays(-i)))
            .ToArray();

        await SeedDesktopAsync(days.Select(day => ("chrome", "Chrome", On(day, 12, 0), 60, 0)).ToArray());

        var cards = await PostAsync(SummaryCardsRoute, new
        {
            dateFrom = Day,
            dateTo = Day.AddDays(4),
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 },
            sources = new[] { "desktop" }
        });

        var card = cards.EnumerateArray().Single();

        card.GetProperty("isNew").GetBoolean().Should().BeFalse();
        card.GetProperty("active").GetProperty("seconds").GetInt32().Should().Be(300);
        card.GetProperty("active").GetProperty("averageSeconds").GetInt32().Should().Be(300,
            "sixty seconds a day over the seven-day lookback, multiplied out to the five days the " +
            "card's own number covers");
        card.GetProperty("active").GetProperty("percentChange").GetDouble().Should().Be(0.0,
            "a span exactly as busy as its baseline is unchanged, not up 400%");
    }

    /// <summary>
    /// An item the lookback never saw has no comparison to draw. A zero baseline would render as an
    /// infinite increase; <c>isNew</c> plus two nulls is the client's cue to draw the card without one.
    /// </summary>
    [Fact]
    public async Task SummaryCards_MarkAnItemWithNoLookbackAsNew()
    {
        await SeedDesktopAsync(("chrome", "Chrome", At(12, 0), 60, 0));

        var card = (await PostAsync(SummaryCardsRoute, Request(AllSources))).EnumerateArray().Single();

        card.GetProperty("isNew").GetBoolean().Should().BeTrue();
        card.GetProperty("active").GetProperty("averageSeconds").ValueKind.Should().Be(JsonValueKind.Null);
        card.GetProperty("active").GetProperty("percentChange").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Android records no background time at all, so an item only the phone saw has no background block
    /// rather than a structurally-always-zero one — a zero would read as "the app was open and idle",
    /// which is a claim the ledger cannot make.
    /// </summary>
    [Fact]
    public async Task SummaryCards_ReportNullBackgroundForAnAndroidOnlyItem()
    {
        await SeedAndroidAsync(("com.slack", "Slack", At(12, 0), At(12, 1), 60));

        var card = (await PostAsync(SummaryCardsRoute, Request(AllSources))).EnumerateArray().Single();

        card.GetProperty("active").GetProperty("seconds").GetInt32().Should().Be(60);
        card.GetProperty("background").ValueKind.Should().Be(JsonValueKind.Null,
            "the phone ledger has no background column, so there is nothing to report rather than zero");
        card.GetProperty("sources").EnumerateArray().Select(s => s.GetString())
            .Should().BeEquivalentTo("android");
    }

    /// <summary>
    /// The default card count is <c>SummaryCardsRequest.DefaultTopN</c> — one number for all four
    /// summary-card routes, because the merged card and the three per-source ones sit on the same
    /// screens and a page showing five of one beside four of another is reading a transcription
    /// difference as a design decision.
    /// </summary>
    [Fact]
    public async Task SummaryCards_DefaultTopNIsTheOneSharedByAllFourRoutes()
    {
        await SeedDesktopAsync(Enumerable.Range(1, 6)
            .Select(i => ($"app{i}", $"App{i}", At(12, i), i * 10, 0))
            .ToArray());

        var cards = (await PostAsync(SummaryCardsRoute, Request(AllSources))).EnumerateArray().ToList();

        cards.Should().HaveCount(5);
        cards.Select(c => c.GetProperty("label").GetString())
            .Should().Equal(["App6", "App5", "App4", "App3", "App2"],
                "the cards are the busiest items in order, and the quietest of the six falls off the end");
    }

    // ---- helpers ---------------------------------------------------------

    private static DateTime At(int hour, int minute) => On(Day, hour, minute);

    /// <summary>The same instant on another day of the span or the lookback.</summary>
    private static DateTime On(DateOnly day, int hour, int minute) =>
        DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Utc);

    /// <summary>A whole-day window over the one seeded day, which is what every test here wants.</summary>
    private static object Request(string[] sources) => new
    {
        dateFrom = Day,
        dateTo = Day,
        from = new { hours = 0, minutes = 0 },
        to = new { hours = 0, minutes = 0 },
        sources
    };

    /// <summary>The same whole-day window, plus the bucket width <c>stacked-bars</c> requires.</summary>
    private static object BarsRequest(string[] sources, int windowMinutes) => new
    {
        dateFrom = Day,
        dateTo = Day,
        from = new { hours = 0, minutes = 0 },
        to = new { hours = 0, minutes = 0 },
        sources,
        windowMinutes
    };

    /// <summary>
    /// One minute the merge has to divide unevenly: the browser holds 35 seconds of it, and the two
    /// desktop processes share the 25 that are left over — 12.5 seconds each, so nothing downstream can
    /// round item by item and still add up.
    /// </summary>
    private async Task SeedFractionalOverlapAsync()
    {
        await SeedWebExtensionAsync(("github.com", At(12, 0), 35, 0));
        await SeedDesktopAsync(
            ("chrome", "Chrome", At(12, 0), 30, 0),
            ("code", "Code", At(12, 0), 30, 0));
    }

    /// <summary>The band boundaries of a stacked-bars response, merged or per source — the two share a shape.</summary>
    private static List<(DateTimeOffset Start, DateTimeOffset End)> Bands(JsonElement response) =>
        response.EnumerateArray()
            .Select(band => (
                Start: band.GetProperty("windowStart").GetDateTimeOffset(),
                End: band.GetProperty("windowEnd").GetDateTimeOffset()))
            .OrderBy(band => band.Start)
            .ToList();

    private static JsonElement SourceRow(JsonElement sources, string name) =>
        sources.EnumerateArray().Single(s => s.GetProperty("source").GetString() == name);

    private async Task SeedDesktopAsync(
        params (string ProcessName, string ProductName, DateTime WindowStart, int Active, int Background)[] rows)
    {
        await using var db = CreateDbContext();

        foreach (var row in rows)
            db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
            {
                UserId = UserId,
                WindowStart = row.WindowStart,
                ProcessName = row.ProcessName,
                ProductName = row.ProductName,
                WindowTitle = "t",
                ExecutablePath = "/usr/bin/app",
                IsFullscreen = false,
                ActiveSeconds = row.Active,
                BackgroundSeconds = row.Background,
                IsPlayingSound = false,
                ActiveMonitor = 0
            });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedWebExtensionAsync(
        params (string Domain, DateTime WindowStart, int Active, int Background)[] rows)
    {
        await using var db = CreateDbContext();

        foreach (var row in rows)
            db.Set<WebExtensionActivityEntry>().Add(new WebExtensionActivityEntry
            {
                UserId = UserId,
                WindowStart = row.WindowStart,
                Domain = row.Domain,
                Url = $"https://{row.Domain}/",
                ActiveSeconds = row.Active,
                BackgroundSeconds = row.Background,
                IsFinal = true
            });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedAndroidAsync(
        params (string PackageName, string AppLabel, DateTime Start, DateTime End, long Seconds)[] sessions)
    {
        await using var db = CreateDbContext();

        foreach (var session in sessions)
            db.Set<AndroidSessionData>().Add(new AndroidSessionData
            {
                UserId = UserId,
                DeviceId = "test-device",
                PackageName = session.PackageName,
                AppLabel = session.AppLabel,
                SessionStartUtc = session.Start,
                SessionEndUtc = session.End,
                DurationSeconds = session.Seconds
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
