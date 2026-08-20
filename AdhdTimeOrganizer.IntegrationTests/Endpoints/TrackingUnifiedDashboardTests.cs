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
    [Fact]
    public async Task Dashboards_RejectAnEmptySourceSelection() =>
        await AssertRejectedAsync([]);

    /// <summary>An unknown member is a typo in a shared link, and should fail rather than quietly narrow the picture.</summary>
    [Fact]
    public async Task Dashboards_RejectAnUnknownSource() =>
        await AssertRejectedAsync(["desktop", "wristwatch"]);

    private async Task AssertRejectedAsync(string[] sources)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-tracking/unified/pie-chart", Request(sources), Json, CancellationToken);

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

    // ---- helpers ---------------------------------------------------------

    private static DateTime At(int hour, int minute) =>
        DateTime.SpecifyKind(Day.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Utc);

    /// <summary>A whole-day window over the one seeded day, which is what every test here wants.</summary>
    private static object Request(string[] sources) => new
    {
        dateFrom = Day,
        dateTo = Day,
        from = new { hours = 0, minutes = 0 },
        to = new { hours = 0, minutes = 0 },
        sources
    };

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
