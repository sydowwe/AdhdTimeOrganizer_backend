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
/// Pins the <c>key</c> / <c>label</c> pair the per-source dashboards carry alongside their own
/// <c>domain</c> / <c>processName</c> / <c>packageName</c> fields.
///
/// <para>The pair exists so a client hashes one field for colour and prints one field for display,
/// rather than six field names across three sources — three chances to hash the display name by
/// mistake and put one application on screen in two colours. It is additive, so nothing here asserts
/// that the original fields went away; they must not.</para>
///
/// <para>The <c>label</c> fallback is the half that can regress silently: drop it and an item whose
/// source has no display name for it renders as an empty string rather than as its own name, on a
/// dashboard that still returns a perfectly good 200.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingDashboardItemIdentityTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Day = new(2026, 6, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// One pair of names on all three sources, sitting beside the source's own fields rather than
    /// replacing them.
    /// </summary>
    [Fact]
    public async Task PieCharts_CarryTheSameIdentityPairOnAllThreeSources()
    {
        await SeedDesktopAsync("slack", "Slack", At(12, 0));
        await SeedWebExtensionAsync("github.com", At(12, 0));
        await SeedAndroidAsync("com.slack", "Slack", At(12, 0));

        var web = await FirstPieItemAsync("web-extension", "domains");
        web.GetProperty("key").GetString().Should().Be("github.com");
        web.GetProperty("label").GetString().Should().Be("github.com");
        web.GetProperty("domain").GetString().Should().Be("github.com",
            "the pair is additive -- the source's own field stays");

        var desktop = await FirstPieItemAsync("desktop", "processes");
        desktop.GetProperty("key").GetString().Should().Be("slack",
            "the process name is the identifier: several processes can ship under one product name, " +
            "so hashing the product name would merge them");
        desktop.GetProperty("label").GetString().Should().Be("Slack");
        desktop.GetProperty("processName").GetString().Should().Be("slack");

        var android = await FirstPieItemAsync("android", "apps");
        android.GetProperty("key").GetString().Should().Be("com.slack");
        android.GetProperty("label").GetString().Should().Be("Slack");
        android.GetProperty("packageName").GetString().Should().Be("com.slack");
    }

    /// <summary>
    /// A desktop row whose product name is blank — an unsigned binary, a portable executable — still
    /// has a name to print. Without the fallback the timeline draws a session labelled with nothing.
    /// </summary>
    [Fact]
    public async Task Timeline_FallsBackToTheIdentifierWhenTheSourceHasNoDisplayName()
    {
        await SeedDesktopAsync("scratch", string.Empty, At(12, 0));
        await SeedDesktopAsync("scratch", string.Empty, At(12, 1));

        var timeline = await PostAsync("/api/activity-tracking/desktop/timeline", new
        {
            dateFrom = Day,
            dateTo = Day,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 }
        });

        var session = timeline.GetProperty("primarySessions").EnumerateArray().First();

        session.GetProperty("key").GetString().Should().Be("scratch");
        session.GetProperty("label").GetString().Should().Be("scratch",
            "label is never blank -- it falls back to the identifier so the client needs no fallback " +
            "of its own");
        session.GetProperty("productName").GetString().Should().BeEmpty(
            "the source's own field keeps reporting what the ledger actually holds");
    }

    // ---- helpers ---------------------------------------------------------

    private static DateTime At(int hour, int minute) =>
        DateTime.SpecifyKind(Day.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Utc);

    private async Task<JsonElement> FirstPieItemAsync(string source, string collection)
    {
        var pie = await PostAsync($"/api/activity-tracking/{source}/pie-chart", new
        {
            dateFrom = Day,
            dateTo = Day,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 0, minutes = 0 }
        });

        return pie.GetProperty(collection).EnumerateArray().First();
    }

    private async Task SeedDesktopAsync(string processName, string productName, DateTime windowStart)
    {
        await using var db = CreateDbContext();

        db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
        {
            UserId = UserId,
            WindowStart = windowStart,
            ProcessName = processName,
            ProductName = productName,
            WindowTitle = "t",
            ExecutablePath = "/usr/bin/app",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedWebExtensionAsync(string domain, DateTime windowStart)
    {
        await using var db = CreateDbContext();

        db.Set<WebExtensionActivityEntry>().Add(new WebExtensionActivityEntry
        {
            UserId = UserId,
            WindowStart = windowStart,
            Domain = domain,
            Url = $"https://{domain}/",
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsFinal = true
        });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedAndroidAsync(string packageName, string appLabel, DateTime start)
    {
        await using var db = CreateDbContext();

        db.Set<AndroidSessionData>().Add(new AndroidSessionData
        {
            UserId = UserId,
            DeviceId = "test-device",
            PackageName = packageName,
            AppLabel = appLabel,
            SessionStartUtc = start,
            SessionEndUtc = start.AddMinutes(1),
            DurationSeconds = 60
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
