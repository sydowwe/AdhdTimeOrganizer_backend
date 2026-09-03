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
/// The places where the four dashboards answered the same question differently: a threshold applied to
/// one pie and not the others, a count taken with a null check on one and with
/// <c>IsNullOrEmpty</c> on the rest, and a per-source default number of cards.
///
/// <para>Every one of these returned a well-formed 200 with a number on it that the page beside it
/// contradicted, so the assertions are on the payload's arithmetic — the parts against the total, one
/// count against the list it describes, and the card counts of the four routes against each other.</para>
///
/// <para>The user is in the fixture's default zone, UTC: none of these are calendar questions, and
/// <see cref="TrackingSummaryCardsBaselineTests"/> already pins the ones that are.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingDashboardParityTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static readonly DateOnly Day = new(2026, 6, 1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// No threshold unless the client sent one. The android pie defaulted <c>minPercent</c> to 1 and
    /// discarded everything under it, so a request that asked for no filter still lost its tail — and
    /// the totals were computed before the filter, so the slices stopped adding up to the number printed
    /// above them.
    /// </summary>
    [Fact]
    public async Task AndroidPieChart_KeepsEveryAppWhenTheClientAskedForNoThreshold()
    {
        await SeedAndroidAsync(("com.example.big", "Big", 10_000), ("com.example.small", "Small", 10));

        var pie = await PostAsync("/api/activity-tracking/android/pie-chart", PieRequest(null));

        var apps = AppSeconds(pie);

        apps.Should().ContainKey("Small",
            "0.1% of the day is under the 1% the endpoint used to apply on its own, and the request " +
            "asked for no threshold at all");
        apps.Values.Sum().Should().Be(TotalSeconds(pie),
            "the slices are what the totals row counts, so they have to add up to it");
    }

    /// <summary>
    /// When a threshold <i>is</i> asked for, what it excludes is folded into an <c>Other</c> slice — the
    /// shape both sibling pies and the merged one already take — rather than dropped on the floor.
    /// </summary>
    [Fact]
    public async Task AndroidPieChart_FoldsTheBelowThresholdTailIntoOtherRatherThanDroppingIt()
    {
        await SeedAndroidAsync(("com.example.big", "Big", 10_000), ("com.example.small", "Small", 10));

        var pie = await PostAsync("/api/activity-tracking/android/pie-chart", PieRequest(5.0));

        var apps = AppSeconds(pie);

        apps.Should().NotContainKey("Small");
        apps.Should().ContainKey("Other");
        apps["Other"].Should().Be(10);
        apps.Values.Sum().Should().Be(TotalSeconds(pie),
            "folding the tail keeps the identity the filter would otherwise break");
    }

    /// <summary>
    /// The desktop totals counted distinct window titles with <c>x.WindowTitle != null</c> on a
    /// non-nullable column — always true — while the per-process list three lines above it used
    /// <c>IsNullOrEmpty</c>. An untitled window therefore counted as a title of its own, and the totals
    /// row printed one more than the slices listed.
    /// </summary>
    [Fact]
    public async Task DesktopPieChart_DoesNotCountAnUntitledWindowAsADistinctTitle()
    {
        await SeedDesktopAsync(("chrome", "GitHub", 60), ("chrome", string.Empty, 60));

        var pie = await PostAsync("/api/activity-tracking/desktop/pie-chart", PieRequest(null));

        var listed = pie.GetProperty("processes").EnumerateArray()
            .Single().GetProperty("windowTitles").GetArrayLength();

        pie.GetProperty("totals").GetProperty("totalWindowTitles").GetInt32().Should().Be(listed).And.Be(1,
            "an empty title is the absence of one, and the list the number describes already says so");
    }

    /// <summary>
    /// The web-extension twin of the same slip: domain-only tracking is a documented case, and a null
    /// check alone counted the empty url it stores as a page.
    /// </summary>
    [Fact]
    public async Task WebExtensionPieChart_DoesNotCountADomainOnlyRowAsAPage()
    {
        await SeedWebExtensionAsync(("github.com", "https://github.com/", 60), ("github.com", string.Empty, 60));

        var pie = await PostAsync("/api/activity-tracking/web-extension/pie-chart", PieRequest(null));

        var listed = pie.GetProperty("domains").EnumerateArray()
            .Single().GetProperty("pages").GetArrayLength();

        pie.GetProperty("totals").GetProperty("totalPages").GetInt32().Should().Be(listed).And.Be(1,
            "a row tracked to the domain only carries no page, which is what the pages list already says");
    }

    /// <summary>
    /// One default number of cards for all four routes. Android answered with four where its two siblings
    /// answered with five, which reaches the screen: the three per-source card rows sit on one page, and
    /// the merged one sits beside them.
    /// </summary>
    [Theory]
    [InlineData("desktop")]
    [InlineData("web-extension")]
    [InlineData("android")]
    [InlineData("unified")]
    public async Task SummaryCards_DefaultToTheSameNumberOfCardsOnEverySource(string source)
    {
        // Six of each, on minutes of their own so the merge has nothing to displace and every route has
        // more items than any plausible default.
        await SeedDesktopAsync(Enumerable.Range(0, 6)
            .Select(i => ($"desktop{i}", $"Desktop {i}", 60)).ToArray());
        await SeedWebExtensionAsync(Enumerable.Range(0, 6)
            .Select(i => ($"web{i}.example", $"https://web{i}.example/", 60)).ToArray());
        await SeedAndroidAsync(Enumerable.Range(0, 6)
            .Select(i => ($"com.example.a{i}", $"Android {i}", 60L)).ToArray());

        var cards = await PostAsync($"/api/activity-tracking/{source}/summary-cards", new
        {
            dateFrom = Day,
            dateTo = Day,
            from = new { hours = 0, minutes = 0 },
            to = new { hours = 23, minutes = 59 },
            sources = new[] { "webExtension", "desktop", "android" }
        });

        cards.GetArrayLength().Should().Be(5,
            "the four summary-card routes share one default, so a page showing five desktop apps beside " +
            "four android ones is a transcription difference rather than a design decision");
    }

    // ---- helpers ---------------------------------------------------------

    private static object PieRequest(double? minPercent) => new
    {
        dateFrom = Day,
        dateTo = Day,
        from = new { hours = 0, minutes = 0 },
        to = new { hours = 23, minutes = 59 },
        minPercent
    };

    private static Dictionary<string, long> AppSeconds(JsonElement pie) =>
        pie.GetProperty("apps").EnumerateArray()
            .ToDictionary(a => a.GetProperty("appLabel").GetString()!, a => a.GetProperty("seconds").GetInt64());

    private static long TotalSeconds(JsonElement pie) =>
        pie.GetProperty("totals").GetProperty("totalSeconds").GetInt64();

    /// <summary>Midday plus <paramref name="minute"/>, so seeded rows never share a minute of wall clock.</summary>
    private static DateTime At(int minute) =>
        DateTime.SpecifyKind(Day.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc).AddMinutes(minute);

    private async Task SeedDesktopAsync(params (string ProcessName, string WindowTitle, int Seconds)[] rows)
    {
        await using var db = CreateDbContext();

        for (var i = 0; i < rows.Length; i++)
            db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
            {
                UserId = UserId,
                WindowStart = At(i),
                ProcessName = rows[i].ProcessName,
                ProductName = rows[i].ProcessName,
                WindowTitle = rows[i].WindowTitle,
                ExecutablePath = $"/usr/bin/{rows[i].ProcessName}",
                IsFullscreen = false,
                ActiveSeconds = rows[i].Seconds,
                BackgroundSeconds = 0,
                IsPlayingSound = false,
                ActiveMonitor = 0
            });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedWebExtensionAsync(params (string Domain, string Url, int Seconds)[] rows)
    {
        await using var db = CreateDbContext();

        for (var i = 0; i < rows.Length; i++)
            db.Set<WebExtensionActivityEntry>().Add(new WebExtensionActivityEntry
            {
                UserId = UserId,
                WindowStart = At(20 + i),
                Domain = rows[i].Domain,
                Url = rows[i].Url,
                ActiveSeconds = rows[i].Seconds,
                BackgroundSeconds = 0,
                IsFinal = true
            });

        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedAndroidAsync(params (string PackageName, string AppLabel, long Seconds)[] sessions)
    {
        await using var db = CreateDbContext();

        for (var i = 0; i < sessions.Length; i++)
            db.Set<AndroidSessionData>().Add(new AndroidSessionData
            {
                UserId = UserId,
                DeviceId = "test-device",
                PackageName = sessions[i].PackageName,
                AppLabel = sessions[i].AppLabel,
                SessionStartUtc = At(40 + i),
                SessionEndUtc = At(40 + i).AddSeconds(sessions[i].Seconds),
                DurationSeconds = sessions[i].Seconds
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
