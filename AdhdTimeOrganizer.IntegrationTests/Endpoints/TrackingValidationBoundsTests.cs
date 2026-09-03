using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.application.dto.request.activityTracking.desktop;
using AdhdTimeOrganizer.Tracking.application.validator;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The validator rules of batch D — <c>CQ-9</c> (lengths), <c>CQ-20</c> (the per-entry seconds message),
/// <c>CQ-14</c> (the two grids that had no validator) and <c>PERF-3</c> (the missing details span cap)
/// in <c>review/tracking/02-findings.md</c>.
///
/// <para>Most of these separate a <b>400 naming the field</b> from a <b>500</b>: without the rule the
/// value reaches Postgres and comes back as a <c>22001</c> the client cannot act on, and every one of
/// these routes belongs to an unattended agent that re-posts the same payload. So the assertions are on
/// <c>BadRequest</c> specifically rather than on "not 2xx" — the failing shape answered too, just with
/// the wrong status.</para>
/// </summary>
[Collection("Postgres")]
public class TrackingValidationBoundsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string HeartbeatRoute = "/api/activity-tracking/desktop/heartbeat";
    private const string DesktopMappingRoute = "/api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern";
    private const string AndroidMappingRoute = "/api/activity-tracking/android/settings/tracker-android-mapping-by-pattern";
    private const string DesktopGridRoute = "/api/activity-tracking/desktop/gird";
    private const string AndroidGridRoute = "/api/activity-tracking/android/gird";

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    // ---- CQ-9: lengths on the ingest path --------------------------------

    [Fact]
    public async Task DesktopHeartbeat_RejectsAWindowTitleWiderThanItsColumn()
    {
        var response = await PostHeartbeatAsync(
            Entry(windowTitle: new string('t', TrackingIngestLimits.MaxWindowTitleLength + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an over-long value is a Postgres 22001 the agent sees as a 500 and retries forever");
    }

    [Fact]
    public async Task DesktopHeartbeat_RejectsAProcessNameWiderThanItsColumn()
    {
        var response = await PostHeartbeatAsync(
            Entry(processName: new string('p', TrackingIngestLimits.MaxProcessNameLength + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The quiet half of <c>CQ-9</c>, and the reason a <c>MaximumLength</c> alone is not the fix: the
    /// title below is 900 <em>characters</em>, comfortably inside the 1024-character column, and 2700
    /// <em>bytes</em>, which is over the ~2704-byte limit on the btree tuple it also has to fit into.
    /// It fails with a different error and the same 500 the length rules were added to prevent.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_RejectsAWindowTitleThatFitsTheColumnButNotTheIndex()
    {
        var title = new string('\u65e5', 900);
        title.Length.Should().BeLessThan(TrackingIngestLimits.MaxWindowTitleLength,
            "the case is meaningless unless the character count passes the column-width rule");
        Encoding.UTF8.GetByteCount(title).Should().BeGreaterThan(TrackingIngestLimits.MaxIndexedEntryTextBytes);

        var response = await PostHeartbeatAsync(Entry(windowTitle: title));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DesktopHeartbeat_StillAcceptsAnOrdinaryEntry()
    {
        var response = await PostHeartbeatAsync(Entry());

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the length rules must not cost the highest-frequency write path its normal traffic");
    }

    // ---- CQ-20: the message on the per-entry seconds cap ------------------

    /// <summary>
    /// The two caps were a <c>.Must(...).Must(...).WithMessage(...)</c> chain, so the message bound to
    /// the second call only and the per-entry one failed with FluentValidation's default text — which
    /// names the predicate, not the rule. The sum's message was also wrong about what it summed.
    /// </summary>
    [Fact]
    public async Task DesktopHeartbeat_NamesThePerEntryCapItFailedRatherThanTheDefaultText()
    {
        var response = await PostHeartbeatAsync(Entry(activeSeconds: 40, backgroundSeconds: 40));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("60 seconds of active and background time",
                "the per-entry cap had no message of its own; the chain gave it to the sum rule instead");
    }

    // ---- CQ-9: lengths on the four mapping validators ---------------------

    [Fact]
    public async Task CreateDesktopMapping_RejectsAPatternWiderThanItsColumn()
    {
        var response = await CreateTrackingClient().PostAsJsonAsync(DesktopMappingRoute, new
        {
            processName = new string('p', TrackingIngestLimits.MaxDesktopMappingPatternLength + 1),
            processNameMatchType = 0,
            isActive = true,
            isIgnored = true
        }, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateAndroidMapping_RejectsAPatternWiderThanItsColumn()
    {
        var response = await CreateTrackingClient().PostAsJsonAsync(AndroidMappingRoute, new
        {
            packageName = new string('p', TrackingIngestLimits.MaxAndroidMappingPatternLength + 1),
            packageNameMatchType = 0,
            isActive = true,
            isIgnored = true
        }, Json, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- CQ-14: the two grids that had no validator at all -----------------

    /// <summary>
    /// <c>ItemsPerPage = 0</c> reaches <c>(int)Math.Ceiling(itemsCount / 0.0)</c> in both handlers, which
    /// is a garbage page count rather than an error; a large value is an unbounded page over a
    /// <c>GROUP BY</c> of the whole partitioned ledger. The two mapping grids beside them in the same
    /// settings screen have bounded this all along.
    /// </summary>
    [Theory]
    [InlineData(DesktopGridRoute)]
    [InlineData(AndroidGridRoute)]
    public async Task DistinctEntriesGrid_RejectsAPageSizeOutsideItsBounds(string route)
    {
        var zero = await CreateTrackingClient().PostAsJsonAsync(route, GridBody(0), Json, CancellationToken);
        zero.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var unbounded = await CreateTrackingClient().PostAsJsonAsync(route, GridBody(10_000), Json, CancellationToken);
        unbounded.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ordinary = await CreateTrackingClient().PostAsJsonAsync(route, GridBody(20), Json, CancellationToken);
        ordinary.StatusCode.Should().Be(HttpStatusCode.OK, "the bounds must not cost the grid its normal page");
    }

    // ---- CQ-15: the mapping grids' target-shape filter ---------------------

    /// <summary>
    /// <c>Type</c> was <c>required</c>, so a page that wanted to narrow on anything else — "show me my
    /// inactive rules" — had to ask for one target shape at a time and stitch the answers together. Null
    /// now means "every shape", and the rest of the filter still applies, which is the half that would
    /// regress silently: a <c>null =&gt; query</c> arm that also skipped the other predicates would return
    /// a superset nobody would read as a bug.
    /// </summary>
    [Fact]
    public async Task MappingGrid_FiltersWithoutATargetShapeWhenTypeIsOmitted()
    {
        var (activeId, inactiveId) = await SeedTwoDesktopMappingsAsync();

        var response = await CreateTrackingClient().PostAsJsonAsync(
            $"{DesktopMappingRoute}/filtered-table",
            new
            {
                useFilter = true,
                filter = new { isActive = false },
                sortBy = Array.Empty<object>(),
                itemsPerPage = 50,
                page = 1
            },
            Json,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a filter without a target shape is now expressible");

        var ids = (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToList();

        ids.Should().Contain(inactiveId);
        ids.Should().NotContain(activeId, "IsActive must still narrow when Type does not");
    }

    /// <summary>
    /// The other half: an undefined value used to reach the switch's <c>_</c> arm and throw, so the client
    /// got a 500 for a bad request of its own making. The converter is configured with integer values
    /// allowed, so a number binds and the validator is what has to reject it.
    /// </summary>
    [Fact]
    public async Task MappingGrid_RejectsAnUndefinedTargetShape()
    {
        var response = await CreateTrackingClient().PostAsJsonAsync(
            $"{DesktopMappingRoute}/filtered-table",
            new
            {
                useFilter = true,
                filter = new { type = 99 },
                sortBy = Array.Empty<object>(),
                itemsPerPage = 50,
                page = 1
            },
            Json,
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an unmapped target shape is a bad request, not an ArgumentOutOfRangeException");
        (await response.Content.ReadAsStringAsync(CancellationToken))
            .Should().Contain("Type", "the 400 has to name the member the client got wrong");
    }

    // ---- PERF-3: the details span cap ------------------------------------

    /// <summary>
    /// The desktop details panel had <c>To &gt; From</c> and no maximum, while its web-extension twin
    /// capped at 31 days. Its read is filtered by process name — a predicate no index supports — so the
    /// span was the only thing bounding a scan of the partitioned ledger.
    /// </summary>
    [Fact]
    public async Task ProcessDetails_RejectsASpanLongerThanTheDetailsCap()
    {
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var beyond = from.AddDays(DashboardDateRangeRules.MaxDetailsSpanDays + 1);

        var response = await CreateTrackingClient().GetAsync(
            $"/api/activity-tracking/desktop/process-details?from={from:O}&to={beyond:O}&processName=chrome",
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ProcessDetails_StillAnswersWithinTheDetailsCap()
    {
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedEntryAsync(from.AddHours(12));

        var response = await CreateTrackingClient().GetAsync(
            $"/api/activity-tracking/desktop/process-details?from={from:O}&to={from.AddDays(DashboardDateRangeRules.MaxDetailsSpanDays):O}&processName=chrome",
            CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a month is the span the panel is opened with; the cap sits exactly at its edge");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// The heartbeat route sits behind <c>PortalAuthorizationPolicies.ActivityTracking</c>, so a plain
    /// User client gets a 403 and never reaches the rules under test. The dashboards and settings grids
    /// accept the same client, so one factory serves the whole class.
    /// </summary>
    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }

    private static DesktopActivityEntryDto Entry(
        string? processName = null,
        string? windowTitle = null,
        int activeSeconds = 30,
        int backgroundSeconds = 0) =>
        new()
        {
            ProcessName = processName ?? "chrome.exe",
            ProductName = "Chrome",
            WindowTitle = windowTitle ?? "a window",
            ExecutablePath = @"C:\test\chrome.exe",
            IsFullscreen = false,
            ActiveSeconds = activeSeconds,
            BackgroundSeconds = backgroundSeconds,
            IsPlayingSound = false,
            ActiveMonitor = 0
        };

    private async Task<HttpResponseMessage> PostHeartbeatAsync(DesktopActivityEntryDto entry)
    {
        var now = DateTime.UtcNow;

        return await CreateTrackingClient().PostAsJsonAsync(HeartbeatRoute, new DesktopActivityWindowDto
        {
            WindowStart = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc),
            Entries = [entry]
        }, Json, CancellationToken);
    }

    /// <summary>
    /// <c>filter: null</c> rather than <c>{}</c>, for the reason <c>TrackingRouteSmokeTests.GridBody</c>
    /// documents: an empty filter object fails deserialization on the required members some filters
    /// carry, so the request would never reach the validator this asserts on.
    /// </summary>
    private static object GridBody(int itemsPerPage) => new
    {
        useFilter = false,
        filter = (object?)null,
        sortBy = Array.Empty<object>(),
        itemsPerPage,
        page = 1
    };

    /// <summary>One active rule and one inactive one, differing only in the field the grid case filters on.</summary>
    private async Task<(long ActiveId, long InactiveId)> SeedTwoDesktopMappingsAsync()
    {
        await using var db = CreateDbContext();

        var active = new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = $"grid-active-{Guid.NewGuid():N}.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            IsIgnored = true
        };
        var inactive = new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = $"grid-inactive-{Guid.NewGuid():N}.exe",
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = false,
            IsIgnored = true
        };

        db.Set<TrackerDesktopMappingByPattern>().AddRange(active, inactive);
        await db.SaveChangesAsync(CancellationToken);

        return (active.Id, inactive.Id);
    }

    private async Task SeedEntryAsync(DateTime windowStart)
    {
        await using var db = CreateDbContext();

        db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
        {
            UserId = UserId,
            WindowStart = DateTime.SpecifyKind(windowStart, DateTimeKind.Utc),
            ProcessName = "chrome",
            ProductName = "chrome",
            WindowTitle = "t",
            ExecutablePath = "/usr/bin/chrome",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        });

        await db.SaveChangesAsync(CancellationToken);
    }
}
