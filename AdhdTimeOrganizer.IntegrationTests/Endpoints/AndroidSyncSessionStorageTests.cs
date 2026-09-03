using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.android;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// What the android sync actually stores for the instants and keys a phone sends (CQ-6, CQ-7 and CQ-22 in
/// <c>review/tracking/02-findings.md</c>). Bounds are pinned separately by
/// <see cref="TrackingIngestBoundsTests"/>; this class is about the rows that pass them.
/// </summary>
/// <remarks>
/// Both failures were silent in different ways. A re-labelled offset stores a real session at the wrong
/// instant, so every dashboard, band and merged minute for it is wrong with nothing to point at; an
/// in-batch duplicate key violated the unique index and 500ed the whole sync, which the device then
/// retried identically forever. Assertions are on stored rows for the first reason and on the response
/// counts for the second.
/// </remarks>
[Collection("Postgres")]
public class AndroidSyncSessionStorageTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    /// <summary>
    /// One instant, spelled two ways: naked (the contract's own reading — the field is
    /// <c>SessionStartUtc</c>) and with a +02:00 offset, which is what a client being more correct than the
    /// contract asks sends. They must land on the same stored value.
    /// </summary>
    /// <remarks>
    /// This can only <em>fail</em> on a host whose local time zone is not UTC: the bug was
    /// <c>RoundtripKind</c> converting the offset-bearing string to the server's local time and
    /// <c>SpecifyKind</c> then stamping Utc on it, so the error is exactly the server's UTC offset and is
    /// zero on a UTC box. It is still worth pinning — the equality is the rule, and the developer machines
    /// this runs on are UTC+1/+2.
    /// </remarks>
    [Fact]
    public async Task AndroidSync_ConvertsAnOffsetBearingInstant_RatherThanRelabellingIt()
    {
        var instant = DateTime.UtcNow.AddHours(-3);
        instant = new DateTime(instant.Ticks - instant.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

        var naked = Session("com.naked", instant.ToString("O"), instant.AddMinutes(15).ToString("O"), 900);
        var offset = Session(
            "com.offset",
            new DateTimeOffset(instant).ToOffset(TimeSpan.FromHours(2)).ToString("O"),
            new DateTimeOffset(instant.AddMinutes(15)).ToOffset(TimeSpan.FromHours(2)).ToString("O"),
            900);

        var response = await PostAndroidSyncAsync([naked, offset]);

        (await AcceptedCountAsync(response)).Should().Be(2);

        var stored = await StoredSessionsAsync();
        stored.Should().HaveCount(2);

        var nakedStart = stored.Single(x => x.PackageName == "com.naked").SessionStartUtc.ToUniversalTime();
        var offsetStart = stored.Single(x => x.PackageName == "com.offset").SessionStartUtc.ToUniversalTime();

        offsetStart.Should().Be(nakedStart,
            "the two strings name the same instant -- an offset must be converted, not stamped over");
        nakedStart.Should().BeCloseTo(instant, TimeSpan.FromSeconds(1),
            "and that shared instant is the one the client meant, not a locale's reading of it");
    }

    /// <summary>
    /// The sync was idempotent <em>across</em> requests and not <em>within</em> one: two items sharing
    /// (package, start) both reached <c>AddRange</c> and violated
    /// <c>ix_android_session_data_user_id_device_id_package_name_session_</c>, taking the whole batch down
    /// with a 500 that the device retried unchanged.
    /// </summary>
    [Fact]
    public async Task AndroidSync_CountsARepeatedKeyWithinOneBatchAsADuplicate()
    {
        var start = DateTime.UtcNow.AddHours(-2);
        var session = Session("com.slack", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900);

        var response = await PostAndroidSyncAsync([session, session]);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a phone re-uploading its buffer must not be able to 500 its own sync forever");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
        body.GetProperty("accepted").GetInt32().Should().Be(1);
        body.GetProperty("duplicatesSkipped").GetInt32().Should().Be(1,
            "the in-batch repeat is reported the same way a repeat of a stored row is");

        (await StoredSessionsAsync()).Should().HaveCount(1);
    }

    /// <summary>
    /// The rest of the batch survives a repeat — the dedup must not swallow the items around it.
    /// </summary>
    [Fact]
    public async Task AndroidSync_StillStoresTheOtherSessionsAroundARepeatedKey()
    {
        var start = DateTime.UtcNow.AddHours(-2);
        var repeated = Session("com.slack", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900);
        var other = Session("com.chrome", start.ToString("O"), start.AddMinutes(15).ToString("O"), 900);

        var response = await PostAndroidSyncAsync([repeated, other, repeated]);

        (await AcceptedCountAsync(response)).Should().Be(2);
        (await StoredSessionsAsync()).Select(x => x.PackageName)
            .Should().BeEquivalentTo(["com.slack", "com.chrome"]);
    }

    /// <summary>
    /// CQ-22: the two instants are free text the endpoint logs verbatim when they fail to parse. A
    /// multi-kilobyte one is a broken or hostile client, so unlike a single unusable session it fails the
    /// whole request rather than being dropped with a warning that carries it into the log file.
    /// </summary>
    [Fact]
    public async Task AndroidSync_RejectsAnInstantStringTooLongToLog()
    {
        var start = DateTime.UtcNow.AddHours(-2);
        var overlong = new string('9', TrackingIngestLimits.MaxInstantStringLength + 1);

        var response = await PostAndroidSyncAsync(
            [Session("com.slack", overlong, start.AddMinutes(15).ToString("O"), 900)]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StoredSessionsAsync()).Should().BeEmpty();
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

    private static AndroidSessionItemDto Session(string packageName, string start, string end, long durationSeconds) =>
        new()
        {
            PackageName = packageName,
            AppLabel = packageName,
            SessionStartUtc = start,
            SessionEndUtc = end,
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

    private async Task<List<AndroidSessionData>> StoredSessionsAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<AndroidSessionData>()
            .IgnoreQueryFilters()
            .Where(x => x.UserId == UserId)
            .ToListAsync(CancellationToken);
    }
}
