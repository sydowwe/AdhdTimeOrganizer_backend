using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.heartbeat;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// What <c>POST /api/activity-tracking/web-extension/heartbeat</c> stores once a payload is past the
/// validator, and which payloads never get there (TEST-1 in <c>review/tracking/testingPrompts/</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the highest-frequency write path in the module — once a minute, per user, per browser — and
/// nothing about it is self-announcing: the response is a bare <c>int</c>, so a derived partition key that
/// stopped being derived, a latch that stopped latching or a URL that got overwritten with an empty string
/// all answer the extension with the same 200 and only show up as a timeline that quietly went wrong.
/// Every assertion below therefore reads the stored row.
/// </para>
/// <para>
/// The neighbouring halves of TEST-1 already have homes and are not repeated here: the auth matrix
/// (extension token, extension token without the ActivityTracking role, plain web JWT) is
/// <c>ExtensionActivityTrackingTests</c>, the per-heartbeat activity cap is
/// <c>TrackingIngestBoundsTests</c>, and the running-totals contract across two heartbeats plus a domain
/// repeated inside one payload is <c>TrackingIngestConflictTests</c> (CQ-5).
/// </para>
/// </remarks>
[Collection("Postgres")]
public class WebExtensionHeartbeatIngestTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string HeartbeatRoute = "/api/activity-tracking/web-extension/heartbeat";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Pinned rather than derived from <c>UtcNow</c>: a run crossing UTC midnight would post into
    /// one day and assert <c>RecordDate</c> against another. Inside the 2026-2030 partitions, and recent
    /// enough that the entity's date query filter keeps the rows visible.</summary>
    private static readonly DateTime NineAm = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    private ITestClientFactory? _trackingFactory;

    // ---- what a heartbeat stores -----------------------------------------

    /// <summary>
    /// One row per domain, and <c>RecordDate</c> — the partition key — derived from <c>WindowStart</c>
    /// rather than set by the endpoint. The derivation is an invariant of the entity (<c>domain-map.md</c>
    /// → Invariants) and a row that lands in the wrong partition is not an error anywhere: it is simply
    /// missing from every dashboard that scans by date.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_InsertsOneRowPerDomain_WithRecordDateDerivedFromWindowStart()
    {
        var response = await PostHeartbeatAsync(NineAm,
        [
            Activity("github.com", activeSeconds: 40),
            Activity("docs.microsoft.com", activeSeconds: 15)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(CancellationToken)).Should().Be(2);

        var stored = await StoredEntriesAsync();
        stored.Should().HaveCount(2, "two domains in one window are two rows, not one folded one");
        stored.Select(x => x.Domain).Should().BeEquivalentTo(["github.com", "docs.microsoft.com"]);
        stored.Should().OnlyContain(x => x.RecordDate == DateOnly.FromDateTime(NineAm),
            "RecordDate is the partition key and is derived from WindowStart -- a row filed under any " +
            "other date is invisible to every dashboard rather than an error");
    }

    /// <summary>
    /// <c>IsFinal</c> is a latch: the extension marks the window closed once, and a heartbeat that arrives
    /// afterwards (a retry, or a tab that woke back up) must not reopen it. The handler only ever assigns
    /// <c>true</c>, never <c>false</c>.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_KeepsIsFinalOnceSet()
    {
        (await PostHeartbeatAsync(NineAm, [Activity("github.com", activeSeconds: 40)], isFinal: true))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostHeartbeatAsync(NineAm, [Activity("github.com", activeSeconds: 45)], isFinal: false))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle();
        stored[0].IsFinal.Should().BeTrue("a closed window stays closed -- the flag latches, it does not track the last request");
        stored[0].ActiveSeconds.Should().Be(45, "the seconds still follow the running total, only the flag latches");
    }

    /// <summary>
    /// The URL is best-effort: a heartbeat that reports no URL for a domain it is still counting seconds
    /// for must leave the last one it did report standing. An assignment that dropped the emptiness check
    /// would blank the column on the very next heartbeat, which is when the user is still on the page.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_KeepsTheLastNonEmptyUrl()
    {
        await PostHeartbeatAsync(NineAm, [Activity("github.com", activeSeconds: 20, url: "https://github.com/x")]);
        await PostHeartbeatAsync(NineAm, [Activity("github.com", activeSeconds: 45, url: "")]);

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle();
        stored[0].Url.Should().Be("https://github.com/x",
            "an empty URL is 'nothing to report', not 'the page has no address'");
    }

    /// <summary>
    /// A domain with no seconds on either counter is dropped before it is stored <b>and</b> before it is
    /// counted. The count is the extension's only feedback, so counting a row that was never written would
    /// read as data loss to whoever compares it against the ledger.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_SkipsZeroSecondActivities()
    {
        var response = await PostHeartbeatAsync(NineAm,
        [
            Activity("idle.com", activeSeconds: 0, backgroundSeconds: 0),
            Activity("github.com", activeSeconds: 40)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(CancellationToken))
            .Should().Be(1, "the response counts activities processed, and a zero-second one is not one of them");

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle("an open tab nobody looked at is not tracking data");
        stored[0].Domain.Should().Be("github.com");
    }

    /// <summary>
    /// The write side of user scoping. The endpoint stamps <c>User.GetId()</c> onto every row it inserts;
    /// nothing else does, and the entity has no default. A row written under the wrong id is another
    /// person's browsing history in this user's timeline. (The read side is
    /// <c>TrackingRouteSmokeTests.WebExtensionActivityEntry_KeepsBothOfItsQueryFilters</c>.)
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_StampsTheCallersUserId()
    {
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
        }

        (await PostHeartbeatAsync(NineAm, [Activity("github.com", activeSeconds: 40)]))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var assertDb = CreateDbContext();
        var all = await assertDb.Set<WebExtensionActivityEntry>()
            .IgnoreQueryFilters()
            .ToListAsync(CancellationToken);

        all.Should().ContainSingle();
        all[0].UserId.Should().Be(UserId, "the row belongs to whoever posted it, not to whoever happens to be first in the table");
    }

    // ---- what never gets past the validator --------------------------------

    /// <summary>
    /// <c>WindowStart</c> must sit on a whole minute. The timeline endpoint joins adjacent windows by
    /// testing <c>WindowStart == previous.EndedAt</c>, so an off-boundary value produces gapped or
    /// overlapping segments silently — and this route is the only place the rule is enforced, because the
    /// entity deliberately does not throw from its setter.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_RejectsAnUnalignedWindowStart_With400()
    {
        var response = await PostHeartbeatAsync(NineAm.AddSeconds(30), [Activity("github.com", activeSeconds: 40)]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a misbehaving client has to be told, because nothing downstream can tell the row apart from a good one");
        (await StoredEntriesAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// A heartbeat covers exactly one minute. Everything downstream — the per-minute activity cap, the
    /// 0-60 second bounds, the timeline's <c>WindowStart.AddMinutes(1)</c> — assumes it, and none of them
    /// reads the field, so a client that widened its window would be believed and silently mis-rendered.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_RejectsWindowMinutesOtherThanOne_With400()
    {
        var response = await PostHeartbeatAsync(NineAm, [Activity("github.com", activeSeconds: 40)], windowMinutes: 5);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredEntriesAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// The anonymous end of the auth matrix, whose three authenticated cases live in
    /// <c>ExtensionActivityTrackingTests</c>. An ingest route reachable without a token is an open write
    /// into an append-only ledger.
    /// </summary>
    [Fact]
    public async Task WebExtensionHeartbeat_Unauthenticated_Returns401()
    {
        using var client = CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync(HeartbeatRoute, Heartbeat(NineAm,
            [Activity("github.com", activeSeconds: 40)]), Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StoredEntriesAsync()).Should().BeEmpty();
    }

    // ---- concurrency: CQ-5 -------------------------------------------------

    /// <summary>
    /// Two heartbeats for the same <c>(WindowStart, Domain)</c> in flight at once — one browser with two
    /// windows open, or a retry racing the request it was retrying. The handler reads the window and then
    /// writes it with no locking, so both can read nothing and both insert, colliding on
    /// <c>ix_web_extension_activity_entry_user_id_window_start_domain_re</c>.
    /// </summary>
    /// <remarks>
    /// The assertion is deliberately weak on status and strong on rows: whether the two requests interleave
    /// is up to the scheduler, so either "one inserted and one updated" (both 200) or "one inserted and one
    /// lost the race" (200 and 409) is a correct outcome. What must never happen is a 500 — the extension
    /// re-posts a rejected batch unchanged, and a 500 on a batch that can never succeed is a loop. That is
    /// what <c>TrySaveIngestBatchAsync</c> answers, and this is the only case that reaches it concurrently.
    /// </remarks>
    [Fact]
    public async Task WebExtensionHeartbeat_ConcurrentPostsForOneWindow_DoNotFailTheBatch()
    {
        var client = CreateTrackingClient();
        var body = Heartbeat(NineAm, [Activity("github.com", activeSeconds: 40)]);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync(HeartbeatRoute, body, Json, CancellationToken),
            client.PostAsJsonAsync(HeartbeatRoute, body, Json, CancellationToken));

        responses.Should().OnlyContain(r => (int)r.StatusCode < 500,
            "a unique-index collision on an ingest route is a 409 the client can act on, never a 500 it re-posts into");

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle("the unique index holds however the two requests interleave");
        stored[0].ActiveSeconds.Should().Be(40);

        foreach (var response in responses)
            response.Dispose();
    }

    // ---- helpers ---------------------------------------------------------

    private static WebExtensionEntryDto Activity(
        string domain, int activeSeconds, int backgroundSeconds = 0, string? url = null) =>
        new()
        {
            Domain = domain,
            Url = url ?? $"https://{domain}/page",
            ActiveSeconds = activeSeconds,
            BackgroundSeconds = backgroundSeconds
        };

    private static WebExtensionHeartbeatRequest Heartbeat(
        DateTime windowStart, List<WebExtensionEntryDto> activities, bool isFinal = false, int windowMinutes = 1) =>
        new()
        {
            HeartbeatAt = windowStart.AddMinutes(1),
            WindowStart = windowStart,
            WindowMinutes = windowMinutes,
            IsFinal = isFinal,
            Activities = activities
        };

    private async Task<HttpResponseMessage> PostHeartbeatAsync(
        DateTime windowStart, List<WebExtensionEntryDto> activities, bool isFinal = false, int windowMinutes = 1) =>
        await CreateTrackingClient().PostAsJsonAsync(
            HeartbeatRoute, Heartbeat(windowStart, activities, isFinal, windowMinutes), Json, CancellationToken);

    /// <summary>
    /// The route sits behind <c>PortalAuthorizationPolicies.ActivityTracking</c>, which a plain User client
    /// does not carry — <c>CreateUserRoleClient()</c> would be a 403 before any of this is reached.
    /// </summary>
    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    private async Task<List<WebExtensionActivityEntry>> StoredEntriesAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<WebExtensionActivityEntry>()
            .IgnoreQueryFilters()
            .Where(x => x.UserId == UserId)
            .OrderBy(x => x.Id)
            .ToListAsync(CancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }
}
