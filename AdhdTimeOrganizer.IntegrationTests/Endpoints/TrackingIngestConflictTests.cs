using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.heartbeat;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The unique-index collisions the ingest and mapping routes can be made to hit (CQ-5, CQ-8 and CQ-11 in
/// <c>review/tracking/02-findings.md</c>). One failure mode across three files: a <c>23505</c> surfacing as
/// a 500 on a client nobody is watching.
/// </summary>
/// <remarks>
/// <para>
/// What makes these worth a behavioural test rather than a status-code one is who is on the other end. A
/// desktop agent, a browser extension and a phone all re-post a rejected batch unchanged, so a 500 on a
/// batch that can never succeed is a loop that runs for as long as the client does — the desktop case
/// needed nothing more exotic than one application open on two monitors, which is why it would have been
/// reported as "tracking stopped working" rather than as a crash.
/// </para>
/// <para>
/// So the assertions are on stored rows and on the seconds in them, not only on the response: a fold that
/// silently dropped one of the two colliding entries, or a dedup that ate the rest of the batch around it,
/// would also stop the 500 while quietly losing the user's time.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TrackingIngestConflictTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string DesktopHeartbeatRoute = "/api/activity-tracking/desktop/heartbeat";
    private const string WebExtensionHeartbeatRoute = "/api/activity-tracking/web-extension/heartbeat";
    private const string DesktopMappingRoute = "/api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    // ---- CQ-8: the desktop window's own index -----------------------------

    /// <summary>
    /// One application, two monitors — the shape that took the whole window down with a 500. The index is
    /// <c>(UserId, WindowStart, RecordDate, ProcessName, WindowTitle)</c> and <c>ActiveMonitor</c> is not in
    /// it, so the two entries are one row: their seconds sum, and the rest of the row describes the busier
    /// of the two.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_FoldsTwoWindowsOfOneApplicationIntoOneRow()
    {
        var windowStart = AlignedWindowStart();

        var response = await PostDesktopHeartbeatAsync(windowStart,
        [
            DesktopEntry("code.exe", "Project", activeSeconds: 20, activeMonitor: 0),
            DesktopEntry("code.exe", "Project", activeSeconds: 30, activeMonitor: 1)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "two windows of one application on two monitors is ordinary desktop use, not a bad request");

        var stored = await StoredDesktopEntriesAsync();
        stored.Should().ContainSingle("the two entries collide on the unique index, so they are one row");
        stored[0].ActiveSeconds.Should().Be(50, "folding must keep the seconds, which is what the ledger counts");
        stored[0].ActiveMonitor.Should().Be(1, "the surviving row describes the busier of the collided entries");
    }

    /// <summary>
    /// The control: entries that differ on a field the index does carry must still be two rows, or the fold
    /// would be quietly losing time rather than preserving it.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_StillStoresEntriesThatDifferOnTheIndexedFields()
    {
        var windowStart = AlignedWindowStart();

        await PostDesktopHeartbeatAsync(windowStart,
        [
            DesktopEntry("code.exe", "Project A", activeSeconds: 20, activeMonitor: 0),
            DesktopEntry("code.exe", "Project B", activeSeconds: 30, activeMonitor: 0)
        ]);

        var stored = await StoredDesktopEntriesAsync();
        stored.Should().HaveCount(2);
        stored.Select(x => x.WindowTitle).Should().BeEquivalentTo(["Project A", "Project B"]);
    }

    /// <summary>
    /// The cross-request half: an agent whose heartbeat timed out after it committed re-posts the same
    /// window. Before this that second attempt collided with its own first one and 500ed, so the agent
    /// re-posted it again, forever.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_IsIdempotentWhenTheAgentRepostsAWindow()
    {
        var windowStart = AlignedWindowStart();
        var entries = new List<DesktopActivityEntryDto> { DesktopEntry("code.exe", "Project", activeSeconds: 30) };

        (await PostDesktopHeartbeatAsync(windowStart, entries)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostDesktopHeartbeatAsync(windowStart, entries)).StatusCode.Should().Be(HttpStatusCode.OK,
            "a retry of a window that did land must not be a 500 the agent repeats");

        var stored = await StoredDesktopEntriesAsync();
        stored.Should().ContainSingle();
        stored[0].ActiveSeconds.Should().Be(30, "the retry is skipped, not added on top of the first attempt");
    }

    // ---- CQ-5: the web-extension window's own index ------------------------

    /// <summary>
    /// Two activities naming one domain in a single payload. The batched dictionary registers the row it
    /// just added, so the repeat updates it instead of inserting a second one — and the extension sends
    /// running totals, so the later figure is the whole minute rather than something to add.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_FoldsARepeatedDomainWithinOneHeartbeat()
    {
        var windowStart = AlignedWindowStart();

        var response = await PostWebExtensionHeartbeatAsync(windowStart,
        [
            WebExtensionEntry("example.com", activeSeconds: 10),
            WebExtensionEntry("example.com", activeSeconds: 25)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await StoredWebExtensionEntriesAsync();
        stored.Should().ContainSingle();
        stored[0].ActiveSeconds.Should().Be(25, "the extension sends running totals, so the last one is the total");
    }

    /// <summary>
    /// The path the per-activity <c>FirstOrDefaultAsync</c> used to serve, now served from the one batched
    /// read: the next heartbeat in the same window updates the existing row, and a domain the window has
    /// not seen still inserts.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_UpdatesTheWindowsExistingRowsAndInsertsTheNewOnes()
    {
        var windowStart = AlignedWindowStart();

        await PostWebExtensionHeartbeatAsync(windowStart, [WebExtensionEntry("example.com", activeSeconds: 10)]);
        await PostWebExtensionHeartbeatAsync(windowStart,
        [
            WebExtensionEntry("example.com", activeSeconds: 40),
            WebExtensionEntry("other.com", activeSeconds: 5)
        ]);

        var stored = await StoredWebExtensionEntriesAsync();
        stored.Should().HaveCount(2);
        stored.Single(x => x.Domain == "example.com").ActiveSeconds.Should().Be(40,
            "the second heartbeat's running total replaces the first's");
        stored.Single(x => x.Domain == "other.com").ActiveSeconds.Should().Be(5);
    }

    // ---- CQ-11: the mapping's unique pattern -------------------------------

    /// <summary>
    /// "Map code.exe to Programming" twice is an ordinary mistake, and the per-user pattern index is the
    /// most user-reachable constraint in the module. The status has always been right — the CRUD bases'
    /// <c>DbUtils.HandleException</c> maps <c>23505</c> to a 409 — so what CQ-11's named fix changes is
    /// the body: the pre-check says which rule the user already has instead of "Unique constraint
    /// violation during Create". Both halves are asserted here, because a pre-check that stopped running
    /// would leave the status passing and only the message gone.
    /// </summary>
    [Fact]
    public async Task CreateDesktopMapping_WithADuplicatePattern_Returns409NamingThePattern()
    {
        var client = CreateUserRoleClient();
        var body = new { processName = "code.exe", processNameMatchType = "Exact", isActive = true, isIgnored = true };

        (await client.PostAsJsonAsync(DesktopMappingRoute, body, JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync(DesktopMappingRoute, body, JsonOpts, CancellationToken);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a repeated pattern is a user mistake, not a server error");
        (await duplicate.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("code.exe", "the user has to be told which of their rules this collides with");
    }

    /// <summary>
    /// The same collision reached through update, which is a route it could not take before CQ-10 — the
    /// update DTO carried the pattern fields all along, so re-typing a pattern another rule already has
    /// was a 500 from the index with nothing naming the offender.
    /// </summary>
    [Fact]
    public async Task UpdateDesktopMapping_OntoAnotherRulesPattern_Returns409NamingThePattern()
    {
        var client = CreateUserRoleClient();

        (await client.PostAsJsonAsync(
                DesktopMappingRoute,
                new { processName = "taken.exe", processNameMatchType = "Exact", isActive = true, isIgnored = true },
                JsonOpts, CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await client.PostAsJsonAsync(
            DesktopMappingRoute,
            new { processName = "mine.exe", processNameMatchType = "Exact", isActive = true, isIgnored = true },
            JsonOpts, CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondId = await second.Content.ReadFromJsonAsync<long>(CancellationToken);

        var collision = await client.PutAsJsonAsync(
            $"{DesktopMappingRoute}/{secondId}",
            new { processName = "taken.exe", processNameMatchType = "Exact", isActive = true, isIgnored = true },
            JsonOpts, CancellationToken);

        collision.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await collision.Content.ReadAsStringAsync(CancellationToken)).Should().Contain("taken.exe");
    }

    /// <summary>
    /// The control the pre-check needs and the index does not: a row must not collide with <b>itself</b>.
    /// An update that changes only the target leaves the pattern tuple exactly as stored, so a uniqueness
    /// check that forgot to exclude the row under edit would make every such edit a 409 — a fix that
    /// breaks the ordinary case while passing the two above.
    /// </summary>
    [Fact]
    public async Task UpdateDesktopMapping_KeepingItsOwnPattern_IsNotAConflict()
    {
        var client = CreateUserRoleClient();

        var created = await client.PostAsJsonAsync(
            DesktopMappingRoute,
            new { processName = "unchanged.exe", processNameMatchType = "Exact", isActive = true, isIgnored = true },
            JsonOpts, CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await created.Content.ReadFromJsonAsync<long>(CancellationToken);

        var update = await client.PutAsJsonAsync(
            $"{DesktopMappingRoute}/{id}",
            new { processName = "unchanged.exe", processNameMatchType = "Exact", isActive = false, isIgnored = true },
            JsonOpts, CancellationToken);

        update.StatusCode.Should().Be(HttpStatusCode.OK,
            "editing a rule without touching its pattern is the commonest update there is");

        await using var db = CreateDbContext();
        var stored = await db.Set<TrackerDesktopMappingByPattern>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(m => m.Id == id, CancellationToken);
        stored.IsActive.Should().BeFalse("the update has to have landed, not merely avoided the 409");
    }

    /// <summary>
    /// The half of the index that surprises: it is declared <c>NULLS NOT DISTINCT</c>, so two rules that
    /// both name only a window title — leaving <c>ProcessName</c> and <c>ProductName</c> null — collide.
    /// Under Postgres' default null handling they would not, which is why this needs its own case rather
    /// than riding on the one above.
    /// </summary>
    [Fact]
    public async Task CreateDesktopMapping_WithADuplicateNullBearingPattern_Returns409()
    {
        var client = CreateUserRoleClient();

        (await client.PostAsJsonAsync(
                DesktopMappingRoute,
                new { windowTitle = "*Slack*", windowTitleMatchType = "Wildcard", isActive = true, isIgnored = true },
                JsonOpts,
                CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync(
            DesktopMappingRoute,
            // Same pattern tuple, different target -- the index does not carry the target.
            new { windowTitle = "*Slack*", windowTitleMatchType = "Wildcard", isActive = false, isIgnored = true },
            JsonOpts,
            CancellationToken);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "NULLS NOT DISTINCT makes two rules that both leave ProcessName null the same pattern");
        (await duplicate.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("*Slack*",
                "the pre-check compares nulls the way the index does — a plain == against a null parameter " +
                "is IS NULL in EF, so this case is also what proves the pre-check catches the null-bearing half");
    }

    // ---- helpers ---------------------------------------------------------

    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }

    /// <summary>Both heartbeat validators reject a window start that is not on a whole minute.</summary>
    private static DateTime AlignedWindowStart()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }

    private static DesktopActivityEntryDto DesktopEntry(
        string processName, string windowTitle, int activeSeconds, int activeMonitor = 0) =>
        new()
        {
            ProcessName = processName,
            ProductName = "Test Product",
            WindowTitle = windowTitle,
            ExecutablePath = $@"C:\test\{processName}",
            IsFullscreen = false,
            ActiveSeconds = activeSeconds,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = activeMonitor
        };

    private static WebExtensionEntryDto WebExtensionEntry(string domain, int activeSeconds) =>
        new()
        {
            Domain = domain,
            Url = $"https://{domain}/page",
            ActiveSeconds = activeSeconds,
            BackgroundSeconds = 0
        };

    private async Task<HttpResponseMessage> PostDesktopHeartbeatAsync(
        DateTime windowStart, List<DesktopActivityEntryDto> entries) =>
        await CreateTrackingClient().PostAsJsonAsync(
            DesktopHeartbeatRoute,
            new DesktopActivityWindowDto { WindowStart = windowStart, Entries = entries },
            Json,
            CancellationToken);

    private async Task<HttpResponseMessage> PostWebExtensionHeartbeatAsync(
        DateTime windowStart, List<WebExtensionEntryDto> activities) =>
        await CreateTrackingClient().PostAsJsonAsync(
            WebExtensionHeartbeatRoute,
            new WebExtensionHeartbeatRequest
            {
                HeartbeatAt = DateTime.UtcNow,
                WindowStart = windowStart,
                WindowMinutes = 1,
                IsFinal = false,
                Activities = activities
            },
            Json,
            CancellationToken);

    private async Task<List<DesktopActivityEntry>> StoredDesktopEntriesAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<DesktopActivityEntry>()
            .IgnoreQueryFilters()
            .Where(x => x.UserId == UserId)
            .ToListAsync(CancellationToken);
    }

    private async Task<List<WebExtensionActivityEntry>> StoredWebExtensionEntriesAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<WebExtensionActivityEntry>()
            .IgnoreQueryFilters()
            .Where(x => x.UserId == UserId)
            .ToListAsync(CancellationToken);
    }
}
