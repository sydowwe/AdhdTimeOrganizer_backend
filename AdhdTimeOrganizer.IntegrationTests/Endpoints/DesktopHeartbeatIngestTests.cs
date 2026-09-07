using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// What <c>POST /api/activity-tracking/desktop/heartbeat</c> does with a batch once it is past the
/// validator: which mapping resolves an entry, what an ignore rule costs the response count, and which
/// seconds reach the attribution seam (TEST-6 in <c>review/tracking/testingPrompts/</c>).
/// </summary>
/// <remarks>
/// <para>
/// The neighbouring halves of TEST-6 already have homes and are deliberately not repeated here: the batch
/// collisions and the re-posted window are <c>TrackingIngestConflictTests</c> (CQ-8), the length and
/// index-byte rules are <c>TrackingValidationBoundsTests</c> (CQ-9), and which of two overlapping rules
/// wins is <c>DesktopHeartbeatMappingPrecedenceTests</c> (CQ-16). What is left is the matcher itself and
/// the attribution edge, and every case below is one an agent produces on an ordinary day.
/// </para>
/// <para>
/// Assertions go to stored rows rather than to the status code, because every failure here is a 200: an
/// ignore rule that stopped applying, a wildcard read as a literal and a regex that matched nothing all
/// answer the agent identically and only show up as time landing on the wrong activity — or on none.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class DesktopHeartbeatIngestTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string HeartbeatRoute = "/api/activity-tracking/desktop/heartbeat";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Pinned rather than derived from <c>UtcNow</c>: a run crossing UTC midnight would seed one
    /// day and assert on another, and the ledger's day totals are what the event carries.</summary>
    private static readonly DateTime NineAm = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    private ITestClientFactory? _trackingFactory;

    // ---- pattern resolution ----------------------------------------------

    /// <summary>
    /// An entry an <c>IsIgnored</c> rule matches is dropped whole: no raw row, and it does not count
    /// towards the response. The count is the agent's only feedback, so "2 in, 1 stored, 2 reported"
    /// would read as data loss to whoever is debugging a gap in their day.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_SkipsAnEntryMatchedByAnIgnoreRule()
    {
        await using (var db = CreateDbContext())
        {
            db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
            {
                UserId = UserId,
                ProcessName = "chrome.exe",
                ProcessNameMatchType = PatternMatchType.Exact,
                IsActive = true,
                IsIgnored = true
            });
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await PostHeartbeatAsync(NineAm,
        [
            Entry("chrome.exe", "GitHub", activeSeconds: 20),
            Entry("code.exe", "Project", activeSeconds: 30)
        ]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(CancellationToken))
            .Should().Be(1, "the response counts entries persisted, and the ignored one is not one of them");

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle("an ignored entry is not stored at all -- that is the point of the rule");
        stored[0].ProcessName.Should().Be("code.exe");
    }

    /// <summary>
    /// The four match modes against one entry, in both directions. Only <c>Exact</c> is exercised anywhere
    /// else, and the three that are not are the ones that can silently degrade: a <c>Wildcard</c> whose
    /// anchors were dropped, or a <c>Regex</c> compiled from an escaped pattern, still attributes the
    /// positive case — which is why each row also posts a value the pattern must <em>not</em> match.
    /// </summary>
    [Theory]
    [InlineData(PatternMatchType.Exact, "chrome.exe", "chrome.exe", "chrome.exe.bak")]
    [InlineData(PatternMatchType.Contains, "hrome", "chrome.exe", "notepad.exe")]
    [InlineData(PatternMatchType.Wildcard, "chr*.ex?", "chrome.exe", "chrome.exe.bak")]
    [InlineData(PatternMatchType.Regex, @"^chro(me|mium)\.exe$", "chromium.exe", "chrome.exe.bak")]
    public async Task DesktopHeartbeat_MatchesEachPatternMode(
        PatternMatchType matchType, string pattern, string matchingProcess, string nonMatchingProcess)
    {
        var activityId = await SeedMappedActivityAsync(mapping =>
        {
            mapping.ProcessName = pattern;
            mapping.ProcessNameMatchType = matchType;
        });

        // A whole minute, not part of one: the ledger's IntTime has minute resolution, so a 30-second
        // attribution reads back as zero and neither direction of this case would prove anything. That
        // truncation is pinned on its own below.
        (await PostHeartbeatAsync(NineAm, [Entry(matchingProcess, "Work", activeSeconds: 60)]))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // A separate, non-contiguous window: the sink extends a row whose end meets the next window's
        // start, so a contiguous second post could not be told apart from the first one growing.
        (await PostHeartbeatAsync(NineAm.AddMinutes(10), [Entry(nonMatchingProcess, "Work", activeSeconds: 60)]))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await StoredEntriesAsync()).Should().HaveCount(2, "both entries are raw rows either way");

        (await AttributedSecondsAsync(activityId)).Should().Be(60,
            $"{matchType} must attribute '{matchingProcess}' and must not attribute '{nonMatchingProcess}'");
    }

    /// <summary>
    /// A user-authored pattern that backtracks catastrophically (<c>SEC-4</c>) must not pin the request
    /// thread. Every mapping runs against every entry of every heartbeat, so one such rule would take the
    /// user's tracking down for as long as the rule exists, and the agent would keep posting into it.
    /// </summary>
    /// <remarks>
    /// The matcher bounds each match with a 100 ms timeout and treats a timed-out pattern as "no match",
    /// so the entry lands raw and unattributed. The client carries its own timeout as well: a regression
    /// here hangs rather than fails, and a hung request would otherwise take the whole suite with it.
    /// </remarks>
    [Fact]
    public async Task DesktopHeartbeat_WithACatastrophicRegexMapping_DoesNotHangTheRequest()
    {
        var activityId = await SeedMappedActivityAsync(mapping =>
        {
            mapping.WindowTitle = "(a+)+$";
            mapping.WindowTitleMatchType = PatternMatchType.Regex;
        });

        var title = new string('a', 40) + "!";

        var stopwatch = Stopwatch.StartNew();
        // A whole minute, so "nothing was attributed" is a statement about the match and not about the
        // ledger's minute resolution (see DesktopHeartbeat_TruncatesASubMinuteAttribution).
        var response = await PostHeartbeatAsync(NineAm, [Entry("evil.exe", title, activeSeconds: 60)]);
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the matcher must bound each pattern rather than let one rule backtrack forever");

        (await StoredEntriesAsync()).Should().ContainSingle("a pattern that cannot be evaluated must not lose the entry");
        (await AttributedSecondsAsync(activityId)).Should().Be(0, "a timed-out pattern counts as no match");
    }

    // ---- attribution -----------------------------------------------------

    /// <summary>
    /// The negative of <c>ExtensionActivityTrackingTests.Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask</c>:
    /// with nothing matching, the raw row is written and nothing else is. "Attributes everything" is the
    /// plausible regression, and it would be invisible — the ledger would simply grow.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_DoesNotAttributeAnEntryWithNoMatchingMapping()
    {
        long activityId;
        long plannerTaskId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"Unmapped {Guid.NewGuid():N}", ct: CancellationToken);
            var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(NineAm), ct: CancellationToken);
            plannerTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
                db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(9, 1), ct: CancellationToken);
        }

        var response = await PostHeartbeatAsync(NineAm, [Entry("unmapped.exe", "Whatever", activeSeconds: 60)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(CancellationToken)).Should().Be(1);

        (await StoredEntriesAsync()).Should().ContainSingle("an unmapped entry is still tracking data");

        await using var assertDb = CreateDbContext();
        (await assertDb.Set<ActivityHistory>().IgnoreQueryFilters().CountAsync(h => h.UserId == UserId, CancellationToken))
            .Should().Be(0, "nothing resolved to an activity, so there is nothing to attribute");

        (await assertDb.Set<PlannerTask>().IgnoreQueryFilters().FirstAsync(t => t.Id == plannerTaskId, CancellationToken))
            .Status.Should().Be(PlannerTaskStatus.NotStarted,
                "ActivityTimeRecordedEvent is published for the activities this batch attributed -- with none, " +
                "no completion may be driven off a task that merely exists");
    }

    /// <summary>
    /// Only <c>ActiveSeconds</c> reaches the attribution seam; background time is stored on the raw row and
    /// counts as nothing on the ledger. Whether that is the right product answer is open — an hour of
    /// music behind other work is arguably that activity's time — but today it is invisible, so it is
    /// pinned: an attributed row exists with a length of zero, not no row and not 40 seconds.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_AttributesActiveSecondsOnly()
    {
        var activityId = await SeedMappedActivityAsync(mapping =>
        {
            mapping.ProcessName = "spotify.exe";
            mapping.ProcessNameMatchType = PatternMatchType.Exact;
        });

        var response = await PostHeartbeatAsync(NineAm,
            [Entry("spotify.exe", "Playlist", activeSeconds: 0, backgroundSeconds: 40)]);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle();
        stored[0].ActiveSeconds.Should().Be(0);
        stored[0].BackgroundSeconds.Should().Be(40, "the raw row keeps what the agent reported");

        await using var assertDb = CreateDbContext();
        var attributed = await assertDb.Set<ActivityHistory>()
            .IgnoreQueryFilters()
            .Where(h => h.UserId == UserId && h.ActivityId == activityId)
            .ToListAsync(CancellationToken);

        attributed.Should().ContainSingle("a matched entry is attributed whatever its active seconds are");
        attributed[0].Length.TotalSeconds.Should().Be(0, "background seconds are not attributed today (TEST-6)");
        attributed[0].EndTimestamp.Should().Be(NineAm, "a zero-second attribution is a zero-length window");
    }

    /// <summary>
    /// A heartbeat that reports less than a full minute of active time attributes <b>nothing</b>: the row
    /// is written with a length of zero and the seconds are gone. Pinned, not endorsed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ActivityHistory.Length</c> is an <c>IntTime</c>, which holds hours and minutes — <c>new
    /// IntTime(30)</c> is zero — and <c>ActivityHistoryTimeAttributionSink</c> constructs one straight from
    /// the entry's <c>ActiveSeconds</c>. So every partial minute a heartbeat reports is truncated, and it
    /// does not accumulate either: the sink only extends a row whose end meets the next window's start, and
    /// a 30-second window ends 30 seconds before the next one begins. Two half-minutes of the same app are
    /// two rows of zero, not one of sixty.
    /// </para>
    /// <para>
    /// It is invisible from every surface — the raw ledger keeps the seconds, so the tracking dashboards
    /// still show the time and only the activity's history is short. The intended behaviour is that a
    /// minute's worth of half-minutes counts as a minute; reaching it means changing what the column
    /// stores, which is why this pins the truncation instead. The case above deliberately posts whole
    /// minutes so that it tests the matcher rather than this.
    /// </para>
    /// </remarks>
    [Fact]
    [Trait("Status", "KnownGap")]
    public async Task DesktopHeartbeat_TruncatesASubMinuteAttributionToZero()
    {
        var activityId = await SeedMappedActivityAsync(mapping =>
        {
            mapping.ProcessName = "partial.exe";
            mapping.ProcessNameMatchType = PatternMatchType.Exact;
        });

        (await PostHeartbeatAsync(NineAm, [Entry("partial.exe", "Work", activeSeconds: 30)]))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await StoredEntriesAsync();
        stored.Should().ContainSingle();
        stored[0].ActiveSeconds.Should().Be(30, "the raw ledger keeps the seconds, which is why this is invisible");

        (await AttributedSecondsAsync(activityId)).Should().Be(0,
            "IntTime holds hours and minutes, so a sub-minute attribution rounds to nothing (TEST-6) -- " +
            "if this now reads 30 the ledger gained sub-minute resolution and this pin should be deleted");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// One activity and one active mapping pointing at it; the caller sets the pattern columns, which is
    /// the only thing that differs between these cases. Exactly one target is set, as
    /// <c>CK_TrackerDesktopMappingByPattern_TargetRequired</c> requires.
    /// </summary>
    private async Task<long> SeedMappedActivityAsync(Action<TrackerDesktopMappingByPattern> configurePattern)
    {
        await using var db = CreateDbContext();

        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"Mapped {Guid.NewGuid():N}", ct: CancellationToken);

        var mapping = new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            IsActive = true,
            ActivityId = activityId
        };
        configurePattern(mapping);

        db.Set<TrackerDesktopMappingByPattern>().Add(mapping);
        await db.SaveChangesAsync(CancellationToken);

        return activityId;
    }

    private static DesktopActivityEntryDto Entry(
        string processName, string windowTitle, int activeSeconds, int backgroundSeconds = 0, int activeMonitor = 0) =>
        new()
        {
            ProcessName = processName,
            ProductName = "Test Product",
            WindowTitle = windowTitle,
            ExecutablePath = $@"C:\test\{processName}",
            IsFullscreen = false,
            ActiveSeconds = activeSeconds,
            BackgroundSeconds = backgroundSeconds,
            IsPlayingSound = false,
            ActiveMonitor = activeMonitor
        };

    private async Task<HttpResponseMessage> PostHeartbeatAsync(DateTime windowStart, List<DesktopActivityEntryDto> entries) =>
        await CreateTrackingClient().PostAsJsonAsync(HeartbeatRoute, new DesktopActivityWindowDto
        {
            WindowStart = windowStart,
            Entries = entries
        }, Json, CancellationToken);

    /// <summary>
    /// The timeout is the catastrophic-regex case's safety net: without it an unbounded match would hang
    /// this request until the whole test run gave up, instead of failing one assertion.
    /// </summary>
    private HttpClient CreateTrackingClient()
    {
        var client = (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private async Task<List<DesktopActivityEntry>> StoredEntriesAsync()
    {
        await using var db = CreateDbContext();
        return await db.Set<DesktopActivityEntry>()
            .IgnoreQueryFilters()
            .Where(x => x.UserId == UserId)
            .OrderBy(x => x.Id)
            .ToListAsync(CancellationToken);
    }

    private async Task<int> AttributedSecondsAsync(long activityId)
    {
        await using var db = CreateDbContext();
        var lengths = await db.Set<ActivityHistory>()
            .IgnoreQueryFilters()
            .Where(h => h.UserId == UserId && h.ActivityId == activityId)
            .Select(h => h.Length)
            .ToListAsync(CancellationToken);
        return lengths.Sum(l => l.TotalSeconds);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }
}
