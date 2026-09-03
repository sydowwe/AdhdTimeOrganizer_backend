using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Which mapping wins when two of them match one entry (CQ-16 in <c>review/tracking/02-findings.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// The heartbeat takes <c>mappings.FirstOrDefault(m =&gt; MatchesPattern(...))</c>, and the list used to
/// arrive in whatever order Postgres returned an unordered read — so the answer to "ignore GitHub
/// everywhere except in Chrome" depended on the plan, not on the rules. It is now ordered by specificity
/// (pattern columns set, descending) and then by id.
/// </para>
/// <para>
/// The general rule is seeded <b>first</b>, so it has the lower id: under the old id-shaped ordering it
/// would win, which is what makes these cases discriminate rather than pass on the arrangement they
/// describe. The assertion is on stored rows, because the losing rule here is an <c>IsIgnored</c> one —
/// the whole difference between the two outcomes is whether the entry exists at all.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class DesktopHeartbeatMappingPrecedenceTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string HeartbeatRoute = "/api/activity-tracking/desktop/heartbeat";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    [Fact]
    public async Task DesktopHeartbeat_LetsTheMoreSpecificMappingWin()
    {
        await SeedOverlappingMappingsAsync();

        var response = await PostHeartbeatAsync("chrome.exe", "sydowwe/AdhdTimeOrganizer - GitHub");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle(
            "the two-column rule matches a strict subset of the one-column rule, so it is the one the user " +
            "meant — the broad ignore rule would have dropped this entry");
        stored[0].ProcessName.Should().Be("chrome.exe");
    }

    /// <summary>
    /// The control: an entry only the broad rule matches must still be ignored, or "more specific wins"
    /// would have degenerated into "the ignore rule never applies".
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_StillAppliesTheBroadMappingToWhatOnlyItMatches()
    {
        await SeedOverlappingMappingsAsync();

        var response = await PostHeartbeatAsync("firefox.exe", "sydowwe/AdhdTimeOrganizer - GitHub");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StoredEntriesAsync()).Should().BeEmpty("nothing narrower matched, so the ignore rule stands");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// "Ignore anything titled *GitHub*, except when it is Chrome — that one is Programming." Seeded
    /// broad-first, so the ids run the wrong way for the answer these cases expect: only the specificity
    /// order puts the two-column rule ahead of the one-column one.
    /// </summary>
    private async Task SeedOverlappingMappingsAsync()
    {
        await using var db = CreateDbContext();

        db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            WindowTitle = "*GitHub*",
            WindowTitleMatchType = PatternMatchType.Wildcard,
            IsActive = true,
            IsIgnored = true
        });
        await db.SaveChangesAsync(CancellationToken);

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(
            db, $"Programming {Guid.NewGuid():N}", ct: CancellationToken);

        db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = "chrome.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            WindowTitle = "*GitHub*",
            WindowTitleMatchType = PatternMatchType.Wildcard,
            IsActive = true,
            // Exactly one target, and deliberately not an ignore one: the two rules have to disagree about
            // what happens to the entry, or neither case could tell which of them ran.
            ActivityId = activityId
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }

    private async Task<HttpResponseMessage> PostHeartbeatAsync(string processName, string windowTitle)
    {
        var now = DateTime.UtcNow;

        return await CreateTrackingClient().PostAsJsonAsync(HeartbeatRoute, new DesktopActivityWindowDto
        {
            WindowStart = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc),
            Entries =
            [
                new DesktopActivityEntryDto
                {
                    ProcessName = processName,
                    ProductName = "Test Product",
                    WindowTitle = windowTitle,
                    ExecutablePath = $@"C:\test\{processName}",
                    IsFullscreen = false,
                    ActiveSeconds = 30,
                    BackgroundSeconds = 0,
                    IsPlayingSound = false,
                    ActiveMonitor = 0
                }
            ]
        }, Json, CancellationToken);
    }

    private async Task<List<DesktopActivityEntry>> StoredEntriesAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<DesktopActivityEntry>()
            .IgnoreQueryFilters()
            .Where(x => x.UserId == UserId)
            .ToListAsync(CancellationToken);
    }
}
