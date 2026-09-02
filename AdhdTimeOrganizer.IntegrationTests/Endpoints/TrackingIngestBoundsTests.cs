using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.heartbeat;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The bounds the three ingest routes put on an untrusted client payload (SEC-5,
/// <c>review/tracking/02-findings.md</c>). All three ledgers are append-only and written straight from a
/// desktop agent, a browser extension or a phone, so anything accepted here is stored permanently.
///
/// <para>The android session length is the one that bites. <c>UnifiedActivityLoader</c> allocates one
/// <c>SourceMinute</c> per minute of a session, so a single stored decade-long row makes every unified
/// dashboard for that user allocate millions of records — and the android dashboards read the same row as
/// one ordinary session, so nothing in the UI points at it. These are behavioural assertions on stored
/// rows rather than on status codes alone because that is the only place the poison row is visible.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingIngestBoundsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    [Fact]
    public async Task AndroidSync_RejectsABatchOverTheSessionCap()
    {
        var sessions = Enumerable
            .Range(0, TrackingIngestLimits.MaxAndroidSessionsPerSync + 1)
            .Select(i => Session(DateTime.UtcNow.AddHours(-2).AddSeconds(i * 2), 1))
            .ToList();

        var response = await PostAndroidSyncAsync(sessions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an unbounded batch is an unbounded write into an append-only ledger");
    }

    [Fact]
    public async Task AndroidSync_RejectsAStatedDurationLongerThanADay()
    {
        var start = DateTime.UtcNow.AddYears(-1);
        var duration = TrackingIngestLimits.MaxAndroidSessionSeconds + 1L;

        var response = await PostAndroidSyncAsync([Session(start, duration)]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountStoredSessionsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AndroidSync_DropsASessionSpanningMoreThanADay()
    {
        // The shape that produced the stored denial of service: a plausible stated duration on a span of
        // years. The endpoint bounds the span itself, not just the number the client claims for it.
        var start = DateTime.UtcNow.AddYears(-1);
        var session = Session(start, TrackingIngestLimits.MaxAndroidSessionSeconds);
        session = session with { SessionEndUtc = start.AddYears(10).ToString("O") };

        var response = await PostAndroidSyncAsync([session]);

        (await AcceptedCountAsync(response)).Should().Be(0,
            "a decade-long session must never reach the ledger -- the unified loader allocates one " +
            "record per minute of it");
        (await CountStoredSessionsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AndroidSync_DropsASessionInTheFuture()
    {
        var start = DateTime.UtcNow.AddDays(1);

        var response = await PostAndroidSyncAsync([Session(start, 3600)]);

        (await AcceptedCountAsync(response)).Should().Be(0,
            "a phone's clock is not authoritative -- a session that has not happened yet is a broken " +
            "clock or a forged payload");
        (await CountStoredSessionsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AndroidSync_DropsASessionOlderThanTheRetentionHorizon()
    {
        // RetentionOptions defaults to three years; the purge job would delete this on its next run, so
        // storing it only creates history nobody can explain.
        var start = DateTime.UtcNow.AddYears(-10);

        var response = await PostAndroidSyncAsync([Session(start, 3600)]);

        (await AcceptedCountAsync(response)).Should().Be(0);
        (await CountStoredSessionsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AndroidSync_StillAcceptsAnOrdinarySession()
    {
        var start = DateTime.UtcNow.AddHours(-2);

        var response = await PostAndroidSyncAsync([Session(start, 900)]);

        (await AcceptedCountAsync(response)).Should().Be(1,
            "the bounds must not cost the route its normal traffic");
        (await CountStoredSessionsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DesktopHeartbeat_RejectsMoreEntriesThanAMinuteCanHold()
    {
        var now = DateTime.UtcNow;
        var request = new DesktopActivityWindowDto
        {
            WindowStart = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc),
            Entries = Enumerable
                .Range(0, TrackingIngestLimits.MaxDesktopEntriesPerWindow + 1)
                .Select(i => new DesktopActivityEntryDto
                {
                    ProcessName = $"proc-{i}.exe",
                    ProductName = "Test",
                    WindowTitle = $"window-{i}",
                    ExecutablePath = $@"C:\test\proc-{i}.exe",
                    IsFullscreen = false,
                    ActiveSeconds = 0,
                    BackgroundSeconds = 1,
                    IsPlayingSound = false,
                    ActiveMonitor = 0
                })
                .ToList()
        };

        var response = await CreateTrackingClient()
            .PostAsJsonAsync("/api/activity-tracking/desktop/heartbeat", request, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WebExtensionHeartbeat_RejectsMoreActivitiesThanAMinuteCanHold()
    {
        var now = DateTime.UtcNow;
        var request = new WebExtensionHeartbeatRequest
        {
            HeartbeatAt = now,
            WindowStart = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc),
            WindowMinutes = 1,
            IsFinal = false,
            Activities = Enumerable
                .Range(0, TrackingIngestLimits.MaxWebExtensionActivitiesPerHeartbeat + 1)
                .Select(i => new WebExtensionEntryDto
                {
                    Domain = $"example-{i}.com",
                    Url = $"https://example-{i}.com/page",
                    ActiveSeconds = 1,
                    BackgroundSeconds = 0
                })
                .ToList()
        };

        var response = await CreateTrackingClient()
            .PostAsJsonAsync("/api/activity-tracking/web-extension/heartbeat", request, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The three ingest routes sit behind <c>PortalAuthorizationPolicies.ActivityTracking</c>, so a plain
    /// User client gets a 403 and never reaches the bounds under test.
    /// </summary>
    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }

    private static AndroidSessionItemDto Session(DateTime start, long durationSeconds) =>
        new()
        {
            PackageName = "com.slack",
            AppLabel = "Slack",
            SessionStartUtc = start.ToString("O"),
            SessionEndUtc = start.AddSeconds(durationSeconds).ToString("O"),
            DurationSeconds = durationSeconds
        };

    private async Task<HttpResponseMessage> PostAndroidSyncAsync(List<AndroidSessionItemDto> sessions) =>
        await CreateTrackingClient().PostAsJsonAsync(
            "/api/activity-tracking/android/sync",
            new AndroidSyncRequest
            {
                DeviceId = "test-device",
                SyncedUpToUtc = DateTime.UtcNow,
                Sessions = sessions
            },
            Json,
            CancellationToken);

    private async Task<int> AcceptedCountAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
        return body.GetProperty("accepted").GetInt32();
    }

    private async Task<int> CountStoredSessionsAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<AndroidSessionData>()
            .IgnoreQueryFilters()
            .CountAsync(x => x.UserId == UserId, CancellationToken);
    }
}
