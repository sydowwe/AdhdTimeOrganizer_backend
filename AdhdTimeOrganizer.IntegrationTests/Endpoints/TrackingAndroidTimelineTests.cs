using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The android timeline's two duration members. <c>durationSeconds</c> is the session's own wall clock
/// and <c>totalSeconds</c> is the tracked activity inside it — the same pair the desktop and
/// web-extension timelines return, which the client's tooltip labels "Duration" and "Active time" and
/// which its median-session-length measure reads.
///
/// <para>On android the two are necessarily equal: the ledger stores real foreground sessions and
/// <c>AndroidSyncEndpoint</c> rejects any whose reported duration disagrees with its own bounds by
/// more than two seconds, so there is no idle remainder inside a session.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingAndroidTimelineTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Day = new(2026, 6, 1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// <c>totalSeconds</c> is per session, not per app.
    ///
    /// <para>It used to be the whole span's total for the app label, repeated on every one of that
    /// app's sessions. Nothing about that is visible in the response shape — it is a plausible number
    /// in the right field on a well-formed 200 — and it is only wrong when an app has more than one
    /// session, which is why the seed below gives it three. The symptom on the client was a tooltip
    /// reading a day total as one session's active time, and any average over sessions coming out
    /// multiplied by the session count.</para>
    /// </summary>
    [Fact]
    public async Task TotalSeconds_IsTheSessionsOwnActivity_NotTheWholeDaysTotalForTheApp()
    {
        await SeedSessionAsync("com.slack", "Slack", new TimeOnly(9, 0), 300);
        await SeedSessionAsync("com.slack", "Slack", new TimeOnly(10, 0), 600);
        await SeedSessionAsync("com.slack", "Slack", new TimeOnly(11, 0), 900);
        await SeedSessionAsync("com.spotify", "Spotify", new TimeOnly(12, 0), 120);

        var sessions = (await PostTimelineAsync())
            .GetProperty("sessions")
            .EnumerateArray()
            .ToList();

        sessions.Should().HaveCount(4);

        foreach (var session in sessions)
            session.GetProperty("totalSeconds").GetInt64().Should()
                .Be(session.GetProperty("durationSeconds").GetInt64(),
                    "an android session is foreground time end to end, so its activity is its length");

        sessions
            .Where(s => s.GetProperty("appLabel").GetString() == "Slack")
            .Select(s => s.GetProperty("totalSeconds").GetInt64())
            .Should().BeEquivalentTo([300L, 600L, 900L],
                "each of the app's three sessions reports its own seconds -- the previous value gave " +
                "all three 1800, the app's total for the day");
    }

    /// <summary>
    /// The filter reads the same member the client's own session-length measures do, so a session is
    /// kept or dropped on its own length rather than on its app's day total — which would keep every
    /// short session of a heavily-used app and drop long ones of a rarely-used one.
    /// </summary>
    [Fact]
    public async Task MinSeconds_FiltersOnTheSessionsOwnLength()
    {
        await SeedSessionAsync("com.slack", "Slack", new TimeOnly(9, 0), 60);
        await SeedSessionAsync("com.slack", "Slack", new TimeOnly(10, 0), 60);
        await SeedSessionAsync("com.slack", "Slack", new TimeOnly(11, 0), 600);

        var sessions = (await PostTimelineAsync(minSeconds: 300))
            .GetProperty("sessions")
            .EnumerateArray()
            .ToList();

        sessions.Should().HaveCount(1,
            "only the ten-minute session clears the threshold -- the two one-minute ones belong to an " +
            "app with twelve minutes on the day, which must not carry them past the filter");
        sessions[0].GetProperty("durationSeconds").GetInt64().Should().Be(600);
    }

    // ---- helpers ---------------------------------------------------------

    private async Task SeedSessionAsync(string packageName, string appLabel, TimeOnly start, long seconds)
    {
        await using var db = CreateDbContext();

        var startUtc = DateTime.SpecifyKind(Day.ToDateTime(start), DateTimeKind.Utc);

        db.Set<AndroidSessionData>().Add(new AndroidSessionData
        {
            UserId = UserId,
            DeviceId = "test-device",
            PackageName = packageName,
            AppLabel = appLabel,
            SessionStartUtc = startUtc,
            SessionEndUtc = startUtc.AddSeconds(seconds),
            DurationSeconds = seconds
        });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<JsonElement> PostTimelineAsync(long? minSeconds = null)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/activity-tracking/android/timeline",
            new
            {
                dateFrom = Day,
                dateTo = Day,
                from = new { hours = 0, minutes = 0 },
                to = new { hours = 0, minutes = 0 },
                minSeconds
            },
            Json,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadFromJsonAsync<JsonElement>(Json, CancellationToken);
    }
}
