using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-2: the parsing, de-duplication and validation <c>AndroidSyncEndpoint</c> does entirely by hand,
/// none of which any base class or convention covers.
///
/// <para>The endpoint <b>silently drops</b> a session it cannot use — unparseable instants, <c>end &lt;=
/// start</c>, a stated duration that disagrees with the span — logging a warning and continuing so that one
/// bad row from a phone's backlog cannot strand the rest of its history. A dropped session is counted in
/// <b>neither</b> <c>accepted</c> nor <c>duplicatesSkipped</c>, which is the only way a client can tell it
/// happened at all, so every drop case here asserts both counters and the row count.</para>
/// </summary>
/// <remarks>
/// Deliberately not repeated here, because they are already pinned:
/// <list type="bullet">
/// <item>offset conversion (CQ-6) and the in-batch repeated key (CQ-7) —
/// <see cref="AndroidSyncSessionStorageTests"/>;</item>
/// <item>the batch cap, the session-length bound, the future and retention-horizon drops (SEC-5) —
/// <see cref="TrackingIngestBoundsTests"/>.</item>
/// </list>
/// </remarks>
[Collection("Postgres")]
public class AndroidSyncIngestTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string Route = "/api/activity-tracking/android/sync";

    private const string Device = "pixel-8";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    // ---- parsing ---------------------------------------------------------

    /// <summary>
    /// The field is named <c>SessionStartUtc</c>, so a client that sends no zone at all has already said
    /// what it means. <c>AssumeUniversal</c> is what makes the endpoint read it that way; without it a
    /// naked instant is read in the <em>server's</em> zone, which makes the stored value depend on where
    /// the process happens to run.
    /// </summary>
    [Fact]
    public async Task AndroidSync_ReadsAZonelessInstantAsUtc()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-3));

        var response = await PostAsync(Session("com.slack", Naked(start), Naked(start.AddMinutes(15)), 900));

        (await AcceptedAsync(response)).Should().Be(1);

        var stored = await StoredAsync();
        stored.Should().ContainSingle()
            .Which.SessionStartUtc.ToUniversalTime()
            .Should().BeCloseTo(start, TimeSpan.FromSeconds(1),
                "a zone-less instant on a field called SessionStartUtc is UTC, not the server's local time");
    }

    /// <summary>
    /// One unusable session must cost exactly itself. A phone syncing after a long offline stretch uploads
    /// its whole backlog in one request; failing the batch would strand every good row in it and the device
    /// would re-post the same broken batch forever.
    /// </summary>
    [Fact]
    public async Task AndroidSync_SkipsAnUnparseableInstant_WithoutFailingTheBatch()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));

        var response = await PostAsync(
            Session("com.broken", "not-an-instant", start.AddMinutes(15).ToString("O"), 900),
            Session("com.slack", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900));

        var body = await BodyAsync(response);
        body.Accepted.Should().Be(1, "the good session in the batch is still stored");
        body.DuplicatesSkipped.Should().Be(0,
            "a dropped session is not a duplicate -- it is counted in neither tally, which is the only " +
            "signal the client gets that it was dropped");

        (await StoredAsync()).Should().ContainSingle().Which.PackageName.Should().Be("com.slack");
    }

    // ---- de-duplication --------------------------------------------------

    /// <summary>
    /// The invariant <c>domain-map.md</c> calls "what makes the sync endpoint's re-send idempotent": a
    /// device that never saw the response to its last sync re-posts the same batch, and must not get a
    /// second copy of every session or a 500 from the unique index.
    /// </summary>
    [Fact]
    public async Task AndroidSync_IsIdempotentAcrossRequests()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));
        var session = Session("com.slack", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900);

        (await AcceptedAsync(await PostAsync(session))).Should().Be(1);

        var replay = await BodyAsync(await PostAsync(session));
        replay.Accepted.Should().Be(0);
        replay.DuplicatesSkipped.Should().Be(1, "the row is already stored under the same (device, package, start)");

        (await StoredAsync()).Should().ContainSingle();
    }

    /// <summary>
    /// Which key wins, and it is the index's: the unique index is
    /// <c>(user_id, device_id, package_name, session_start_utc)</c> while the in-request dedup key is only
    /// <c>(package, start)</c> — the device is fixed for a request, so the two agree. Two phones reporting
    /// the same app at the same second are two real sessions and both are kept.
    /// </summary>
    [Fact]
    public async Task AndroidSync_TreatsADifferentDeviceAsADifferentSession()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));
        var session = Session("com.slack", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900);

        (await AcceptedAsync(await PostAsync(Device, session))).Should().Be(1);
        (await AcceptedAsync(await PostAsync("pixel-tablet", session))).Should().Be(1,
            "the stored-row lookup is scoped to the request's device, so the other phone's row is not a duplicate");

        (await StoredAsync()).Should().HaveCount(2)
            .And.OnlyHaveUniqueItems(x => x.DeviceId);
    }

    // ---- validation and bounds -------------------------------------------

    /// <summary>
    /// A zero-length session carries no time and would still allocate a row and a dashboard slice, so it is
    /// a 400 rather than a silent drop — a client sending one is broken in a way it can fix.
    /// </summary>
    [Fact]
    public async Task AndroidSync_RejectsAZeroDuration_With400()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));

        var response = await PostAsync(Session("com.slack", start.ToString("O"), start.ToString("O"), 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AndroidSync_RejectsAnEmptyPackageName_With400()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));

        var response = await PostAsync(Session("", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the package name is half the dedup key and half of every android dashboard's grouping");
        (await StoredAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// The stated duration is what every dashboard sums; the two instants are what the unified loader
    /// slices. When they disagree by more than the two-second tolerance there is no way to tell which one
    /// is the lie, so the session is dropped rather than stored with one of them silently preferred.
    /// </summary>
    [Fact]
    public async Task AndroidSync_SkipsASessionWhoseStatedDurationDisagreesWithItsSpan()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));

        var body = await BodyAsync(await PostAsync(
            Session("com.slack", start.ToString("O"), start.AddMinutes(10).ToString("O"), 60)));

        body.Accepted.Should().Be(0);
        body.DuplicatesSkipped.Should().Be(0);
        (await StoredAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// An empty span passes the validator — the stated duration is positive — and is caught by the
    /// handler's <c>end &lt;= start</c> guard instead. Without it the row would be a session that occupies
    /// no time but still holds a slot in the unique index for that (device, package, start).
    /// </summary>
    [Fact]
    public async Task AndroidSync_SkipsASessionThatEndsWhereItStarts()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));

        var body = await BodyAsync(await PostAsync(Session("com.slack", start.ToString("O"), start.ToString("O"), 1)));

        body.Accepted.Should().Be(0);
        body.DuplicatesSkipped.Should().Be(0);
        (await StoredAsync()).Should().BeEmpty();
    }

    // ---- auth ------------------------------------------------------------

    /// <summary>
    /// <c>[AllowExtensionClients]</c> only lifts the blanket deny — it grants nothing. The
    /// <c>Policies(ActivityTracking)</c> is what keeps an ordinary web session out of the ingest ledger,
    /// and dropping it is a silent widening: the route would fall back to the configurator's
    /// <c>Roles("User","Admin","Root")</c> and keep answering 200.
    /// </summary>
    [Fact]
    public async Task AndroidSync_WithoutTheActivityTrackingRole_Returns403()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));
        var request = Request(Device, [Session("com.slack", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900)]);

        var response = await CreateUserRoleClient().PostAsJsonAsync(Route, request, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AndroidSync_Unauthenticated_Returns401()
    {
        var start = TruncateToSecond(DateTime.UtcNow.AddHours(-2));
        var request = Request(Device, [Session("com.slack", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900)]);

        var response = await CreateUnauthenticatedClient().PostAsJsonAsync(Route, request, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StoredAsync()).Should().BeEmpty();
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The ingest routes sit behind <c>PortalAuthorizationPolicies.ActivityTracking</c>, so the default
    /// client never reaches the handler. Same shape as <see cref="TrackingIngestBoundsTests"/>.
    /// </summary>
    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }

    /// <summary>
    /// Postgres stores microseconds and the round-trip through a string is second-resolution, so the
    /// instant under test is truncated before it is ever sent.
    /// </summary>
    private static DateTime TruncateToSecond(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    /// <summary>An ISO-8601 instant carrying no zone designator at all.</summary>
    private static string Naked(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss");

    private static AndroidSessionItemDto Session(string packageName, string start, string end, long durationSeconds) =>
        new()
        {
            PackageName = packageName,
            AppLabel = packageName.Length == 0 ? "Slack" : packageName,
            SessionStartUtc = start,
            SessionEndUtc = end,
            DurationSeconds = durationSeconds
        };

    private static AndroidSyncRequest Request(string deviceId, List<AndroidSessionItemDto> sessions) =>
        new()
        {
            DeviceId = deviceId,
            SyncedUpToUtc = DateTime.UtcNow,
            Sessions = sessions
        };

    private Task<HttpResponseMessage> PostAsync(params AndroidSessionItemDto[] sessions) =>
        PostAsync(Device, sessions);

    private async Task<HttpResponseMessage> PostAsync(string deviceId, params AndroidSessionItemDto[] sessions) =>
        await CreateTrackingClient().PostAsJsonAsync(Route, Request(deviceId, [.. sessions]), Json, CancellationToken);

    private async Task<SyncResult> BodyAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
        return new SyncResult(
            body.GetProperty("accepted").GetInt32(),
            body.GetProperty("duplicatesSkipped").GetInt32());
    }

    private async Task<int> AcceptedAsync(HttpResponseMessage response) => (await BodyAsync(response)).Accepted;

    private async Task<List<AndroidSessionData>> StoredAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<AndroidSessionData>()
            .IgnoreQueryFilters()
            .Where(x => x.UserId == UserId)
            .ToListAsync(CancellationToken);
    }

    private sealed record SyncResult(int Accepted, int DuplicatesSkipped);
}
