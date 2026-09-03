using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>CQ-12</c> in <c>review/tracking/02-findings.md</c>: <c>PatternMatchType.Regex</c> has to mean a
/// regex on both surfaces that take it.
/// </summary>
/// <remarks>
/// <para>
/// The ingest path evaluates a user's mapping rule with <c>Regex.IsMatch</c>; the settings grids run the
/// same enum through <c>ApplyStringMatchFilter</c>, which translated <c>Regex</c> to a SQL <c>LIKE</c>
/// with the regex as the pattern. Both surfaces answered, neither errored, and the answers were about
/// different questions — the user's way of asking "which of my entries does this rule catch?" was the one
/// that lied.
/// </para>
/// <para>
/// So the assertions are on which rows come back for patterns whose two readings disagree, not on the
/// status code: <c>.*</c> matches everything as a regex and nothing as a LIKE, <c>%</c> the other way
/// round. A status-code test passes just as happily against the broken translation.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TrackingPatternMatchSemanticsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private const string DesktopGridRoute = "/api/activity-tracking/desktop/gird";

    /// <summary>The wire value of <c>PatternMatchType.Regex</c>; the converter binds the integer.</summary>
    private const int RegexMatchType = 3;

    private static readonly string[] TrackingRoles = ["User", "ActivityTracking"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ITestClientFactory? _trackingFactory;

    [Fact]
    public async Task DistinctEntriesGrid_ReadsARegexPatternAsARegex()
    {
        var token = Token();
        await SeedEntryAsync($"chrome-{token}.exe");
        await SeedEntryAsync($"firefox-{token}.exe");

        var matched = await FilterByProcessNameAsync($"^chrome-{token}.*\\.exe$");

        matched.Should().Contain($"chrome-{token}.exe",
            "the anchors and .* are regex syntax the LIKE translation matched literally, so this returned nothing");
        matched.Should().NotContain($"firefox-{token}.exe");
    }

    /// <summary>
    /// The other direction, and the one a user would never report as a filter bug: <c>%</c> is a literal
    /// in a regex and "everything" in a LIKE, so the broken translation answered a pattern that catches
    /// one entry with the user's whole ledger.
    /// </summary>
    [Fact]
    public async Task DistinctEntriesGrid_DoesNotTreatASqlWildcardAsOneUnderARegexMatch()
    {
        var token = Token();
        await SeedEntryAsync($"chrome-{token}.exe");

        var matched = await FilterByProcessNameAsync("%");

        matched.Should().NotContain($"chrome-{token}.exe",
            "'%' matches only a process name containing a per-cent sign once Regex means regex");
    }

    /// <summary>
    /// The ingest matcher compiles every pattern with <see cref="System.Text.RegularExpressions.RegexOptions.IgnoreCase"/>,
    /// so the grid has to be the case-insensitive <c>~*</c> rather than <c>~</c> — otherwise the two
    /// surfaces still disagree, just about a narrower set of patterns.
    /// </summary>
    [Fact]
    public async Task DistinctEntriesGrid_MatchesCaseInsensitivelyLikeTheIngestMatcher()
    {
        var token = Token();
        await SeedEntryAsync($"chrome-{token}.exe");

        var matched = await FilterByProcessNameAsync($"^CHROME-{token.ToUpperInvariant()}");

        matched.Should().Contain($"chrome-{token}.exe");
    }

    /// <summary>
    /// A pattern that does not parse used to be a harmless LIKE; as a <c>~*</c> operand it is an error
    /// from Postgres the endpoint's catch-all reports as a 500. The pattern is the user's, so it is a 400.
    /// </summary>
    [Fact]
    public async Task DistinctEntriesGrid_RejectsAnUnparseableRegex()
    {
        var response = await PostFilterAsync("[unterminated");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "an invalid regex is a bad request, not an internal error");
    }

    // ---- helpers ---------------------------------------------------------

    private static string Token() => Guid.NewGuid().ToString("N")[..8];

    private HttpClient CreateTrackingClient() =>
        (_trackingFactory ??= CreateFactory(TrackingRoles, UserId)).CreateClient();

    public override async ValueTask DisposeAsync()
    {
        if (_trackingFactory is not null)
            await _trackingFactory.DisposeAsync();

        await base.DisposeAsync();
    }

    private async Task<List<string>> FilterByProcessNameAsync(string pattern)
    {
        var response = await PostFilterAsync(pattern);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken))
            .GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("processName").GetString()!)
            .ToList();
    }

    private async Task<HttpResponseMessage> PostFilterAsync(string pattern) =>
        await CreateTrackingClient().PostAsJsonAsync(
            DesktopGridRoute,
            new
            {
                useFilter = true,
                filter = new { processName = pattern, processNameMatchType = RegexMatchType },
                sortBy = Array.Empty<object>(),
                itemsPerPage = 50,
                page = 1
            },
            Json,
            CancellationToken);

    private async Task SeedEntryAsync(string processName)
    {
        await using var db = CreateDbContext();

        db.Set<DesktopActivityEntry>().Add(new DesktopActivityEntry
        {
            UserId = UserId,
            WindowStart = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            ProcessName = processName,
            ProductName = processName,
            WindowTitle = processName,
            ExecutablePath = $"/usr/bin/{processName}",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        });

        await db.SaveChangesAsync(CancellationToken);
    }
}
