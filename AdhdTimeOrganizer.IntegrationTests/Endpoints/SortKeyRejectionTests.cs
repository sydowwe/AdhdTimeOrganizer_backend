using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.@enum;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins what happens to a sort key that resolves to <b>no member at all</b> — the general case behind the
/// four grids <see cref="ProfileGridSortKeyTests"/> fixed one column at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>The behaviour.</b> An unresolvable key is an <c>InvalidSortKeyException</c> out of the sorting
/// helpers, which <c>InvalidSortKeyMiddleware</c> answers with a <b>400 naming the key</b>. Both halves
/// matter and both are asserted here: it must not be a <b>500</b> (a sort key is client input, and an
/// opaque error id says "server bug" about a client sending a column it was never offered — which is how
/// this was filed three times as three endpoint bugs), and it must not be <b>ignored</b> (a skipped key
/// returns a plausible-looking page in the default order, and on a paginated grid a wrong order means rows
/// the caller never sees — indistinguishable from real emptiness to a user).
/// </para>
/// <para>
/// <b>Why the routes below and not one.</b> The rejection lives in a middleware precisely because the three
/// kinds of caller do not share an endpoint base: <c>BaseGridEndpoint</c> derivatives, hand-written
/// endpoints that call <c>GetGridDataAsync</c> themselves (<c>reminder-dashboard/my-upcoming</c>), and the
/// two tracking distinct-entry tables, which additionally wrap their handler in a
/// <c>catch (Exception)</c> that turned every fault into "An internal error occurred." with a 500 — so
/// they have to let this one type back out. A fix in the bases would have left the other two answering 500
/// for the same client mistake.
/// </para>
/// <para>
/// Each route also gets a <b>valid</b> key, because a rejection that is too eager is the same outage in the
/// other direction — and because the mapping and reminder grids' own column keys had no coverage at all.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class SortKeyRejectionTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private const string BacklogGridRoute = "api/activity-backlog-profile/grid";
    private const string MyUpcomingRemindersRoute = "api/reminder-dashboard/my-upcoming";
    private const string DesktopDistinctGridRoute = "api/activity-tracking/desktop/grid";
    private const string DesktopMappingGridRoute = "api/activity-tracking/desktop/settings/tracker-desktop-mapping-by-pattern/grid";

    private static object GridBody(object[] sortBy, object? filter = null) => new
    {
        page = 1,
        itemsPerPage = 100,
        sortBy,
        useFilter = filter is not null,
        filter
    };

    private static object[] SortBy(string key, bool isDesc = false) => [new { key, isDesc }];

    private async Task<(HttpStatusCode Status, string Body)> PostAsync(string route, object body)
    {
        var response = await CreateClient().PostAsJsonAsync(route, body, JsonOpts, CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    /// <summary>
    /// A 400 whose <c>errors.sortBy</c> names the offending key — the same shape the grid base's own page
    /// and page-size errors take, so a client reads one place for all three.
    /// </summary>
    private async Task AssertRejectedAsync(string route, string key)
    {
        var (status, body) = await PostAsync(route, GridBody(SortBy(key)));

        status.Should().Be(HttpStatusCode.BadRequest,
            $"'{key}' resolves to no member, which is the caller naming a column that does not exist — not a server fault");

        body.Should().NotContain("errorId",
            "an opaque error id is the unhandled-500 body; a rejected sort key has to be diagnosable from the response");

        var errors = JsonDocument.Parse(body).RootElement.GetProperty("errors");
        errors.TryGetProperty("sortBy", out var sortByErrors).Should().BeTrue(
            "the error belongs to the request property that carried the key");
        sortByErrors.EnumerateArray().Select(e => e.GetString()).Should().Contain(m => m!.Contains(key),
            "the client cannot fix a key the response does not name");
    }

    private async Task<JsonElement> AssertAcceptedAsync(string route, string key)
    {
        var (status, body) = await PostAsync(route, GridBody(SortBy(key)));

        status.Should().Be(HttpStatusCode.OK,
            $"'{key}' is a member of the projected response — rejecting it would be the same outage in the other direction");

        return JsonDocument.Parse(body).RootElement;
    }

    private static string[] ItemStrings(JsonElement grid, params string[] path) =>
        grid.GetProperty("items").EnumerateArray()
            .Select(item => path.Aggregate(item, (current, segment) => current.GetProperty(segment)).GetString()!)
            .ToArray();

    // ---- A: a grid base ---------------------------------------------------------------------------

    /// <summary>
    /// Both shapes of unresolvable key on a <c>BaseGridEndpoint</c>. The dotted one is here because an
    /// earlier reading of this bug blamed <em>nesting</em> — a key containing a dot — when the trigger is
    /// only ever <em>unresolvable</em>: <c>activity.name</c> works and <c>activity.zzz</c> does not, and a
    /// dot-free non-column fails identically to both.
    /// </summary>
    [Theory]
    [InlineData("zzzNotAColumn")]
    [InlineData("activity.zzzNotAColumn")]
    [InlineData("energyDemandId")]
    public async Task Grid_RejectsAKeyThatResolvesToNoMember(string key)
    {
        await using (var db = CreateDbContext())
            await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Anything");

        await AssertRejectedAsync(BacklogGridRoute, key);
    }

    /// <summary>
    /// A key that <em>resolves</em> but lands on a nested object — <c>locationType</c> is a whole
    /// <c>SelectOptionResponse</c>, <c>activity</c> a whole <c>ActivityResponse</c> — is refused the same
    /// way, not handed to the provider. It used to be: nothing can ORDER BY an object, so it was a 500
    /// (<c>errorId</c>) behind an empty table, and it is exactly the key a
    /// <c>TableColumn('locationType', …, sortable)</c> emits unless its author already knows to send the
    /// leaf. The hint names that leaf, so the 400 is also the fix.
    /// </summary>
    [Theory]
    [InlineData("locationType", "locationType.text")]
    [InlineData("activity", "activity.name")]
    public async Task Grid_RejectsAKeyThatResolvesToANestedObject_AndNamesALeafToSortByInstead(string key, string leaf)
    {
        await using (var db = CreateDbContext())
            await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Anything");

        await AssertRejectedAsync(BacklogGridRoute, key);

        var (_, body) = await PostAsync(BacklogGridRoute, GridBody(SortBy(key)));
        body.Should().Contain(leaf,
            "the object is not sortable but its scalar members are, and the client cannot guess which spelling it wanted");

        await AssertAcceptedAsync(BacklogGridRoute, leaf);
    }

    /// <summary>
    /// The rejection is narrow: the grid's own keys and the no-sort page are untouched. <c>sortBy: []</c>
    /// is the case the board views send and the one that kept working throughout the outage, which is why
    /// the table views looked empty while the board views did not.
    /// </summary>
    [Fact]
    public async Task Grid_StillAcceptsItsOwnKeysAndAnEmptySort()
    {
        await using var db = CreateDbContext();
        foreach (var name in new[] { "Zebra", "Apple" })
            await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, name);

        var sorted = await AssertAcceptedAsync(BacklogGridRoute, "activity.name");
        ItemStrings(sorted, "activity", "name").Should().Equal("Apple", "Zebra");

        var (status, body) = await PostAsync(BacklogGridRoute, GridBody([]));
        status.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(body).RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// The echoed key is capped. It is caller-supplied text, and an error body is not a mirror for
    /// arbitrarily large input — while a client that sent a 200-character key still gets enough of it back
    /// to recognise which column it came from.
    /// </summary>
    [Fact]
    public async Task Grid_RejectingAnAbsurdlyLongKey_EchoesOnlyItsHead()
    {
        var key = new string('z', 200);

        var (status, body) = await PostAsync(BacklogGridRoute, GridBody(SortBy(key)));

        status.Should().Be(HttpStatusCode.BadRequest);
        body.Should().NotContain(key, "the whole key is not echoed back");
        body.Should().Contain(new string('z', 64), "enough of it to identify the column still is");
    }

    // ---- B: a hand-written endpoint that is not a grid base ---------------------------------------

    /// <summary>
    /// <c>reminder-dashboard/my-upcoming</c> is not a <c>/grid</c> route and derives from no grid base — it
    /// calls <c>GetGridDataAsync</c> itself. It is in here as the proof that the rejection lives in the
    /// shared layer rather than in one endpoint.
    /// </summary>
    [Fact]
    public async Task MyUpcomingReminders_RejectsAnUnresolvableKeyAndAcceptsTheRealOne()
    {
        await AssertRejectedAsync(MyUpcomingRemindersRoute, "zzzNotAColumn");
        await AssertAcceptedAsync(MyUpcomingRemindersRoute, "nextOccurrenceAt");
    }

    /// <summary>
    /// <c>nextOccurrence</c> — the spelling this view shipped sending — is accepted as an <b>alias</b> for
    /// the member, on all three reads over a reminder definition. A 400 would be correct and still useless
    /// to the person looking at the screen: the table component renders its empty state for a failed
    /// request, so a diagnosable error and a genuinely empty list look identical to them.
    /// </summary>
    /// <remarks>
    /// Asserted on <b>order</b>, not on a 200: an alias that resolved to the wrong member — or a normalizer
    /// that dropped the entry instead of renaming it — still returns a valid page. The later reminder is
    /// seeded first so the id order is the reverse of the expected one.
    /// </remarks>
    [Theory]
    [InlineData("nextOccurrence")]
    [InlineData("nextOccurrenceAt")]
    public async Task MyUpcomingReminders_AcceptsTheShippedClientSpellingOfItsOneColumn(string key)
    {
        await CreateReminderAsync(DateTime.UtcNow.AddDays(5));
        await CreateReminderAsync(DateTime.UtcNow.AddDays(2));

        var page = await AssertAcceptedAsync(MyUpcomingRemindersRoute, key);

        OccurrencesOf(page).Should().HaveCount(2).And.BeInAscendingOrder(
            "both spellings name the same column, so both order by it");
    }

    /// <summary>
    /// And the admin views over the same row answer the same key set — one contract per row, not one per
    /// endpoint, which is what let the two spellings diverge in the first place.
    /// </summary>
    [Theory]
    [InlineData("api/reminder-dashboard/upcoming")]
    [InlineData("api/reminder-definition/grid")]
    public async Task AdminReminderReads_AcceptTheSameSpellings(string route)
    {
        await CreateReminderAsync(DateTime.UtcNow.AddDays(5));
        await CreateReminderAsync(DateTime.UtcNow.AddDays(2));

        OccurrencesOf(await AssertAcceptedAsync(route, "nextOccurrence")).Should().BeInAscendingOrder();
        OccurrencesOf(await AssertAcceptedAsync(route, "nextOccurrenceAt")).Should().BeInAscendingOrder();

        await AssertRejectedAsync(route, "zzzNotAColumn");
    }

    private static DateTimeOffset[] OccurrencesOf(JsonElement page) =>
        page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("nextOccurrenceAt").GetDateTimeOffset())
            .ToArray();

    /// <summary>
    /// A reminder owned by the caller, created through the real <c>POST api/reminder</c> so the module-side
    /// definition and its explicit recipient row exist as production writes them — seeding the portal row
    /// directly leaves no definition at all, which these three reads are over.
    /// </summary>
    private async Task CreateReminderAsync(DateTime remindAt)
    {
        var response = await CreateClient().PostAsJsonAsync("api/reminder", new
        {
            Title = $"Reminder {Guid.NewGuid():N}",
            RemindAt = remindAt,
            LeadOffsetsMinutes = new[] { 0 }
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the fixture itself must succeed before any ordering assertion built on it means anything");
    }

    // ---- C: the two endpoints that swallowed everything -------------------------------------------

    /// <summary>
    /// The distinct-entry table wraps its whole handler in a <c>catch (Exception)</c> that answers
    /// <c>500 "An internal error occurred."</c>. That catch is worth keeping — it is what stops a provider
    /// fault leaking a message — but it also ate the sort-key rejection and reported a client mistake as a
    /// server fault, so the one type is rethrown past it. Nothing but a request like this shows the
    /// difference: the route, the roles and the body are otherwise identical.
    /// </summary>
    [Fact]
    public async Task DistinctEntriesGrid_RejectsAnUnresolvableKeyRatherThanSwallowingIt()
    {
        await AssertRejectedAsync(DesktopDistinctGridRoute, "zzzNotAColumn");
        await AssertAcceptedAsync(DesktopDistinctGridRoute, "processName");
    }

    // ---- D: the mapping grid's own activity key ---------------------------------------------------

    /// <summary>
    /// The mapping grid's activity column sends <c>activity.text</c> — <c>ActivityFilterFormResponse.Text</c>
    /// holds the activity's <em>name</em>, so the key resolves natively and needs no remap. Pinned because
    /// the audit that swept every column definition in the app flagged it as unverified, and because the
    /// nested object is built <b>conditionally</b> (<c>Activity == null ? null : new …</c>) — the shape
    /// <c>SortByMany</c>'s remarks call out as the one a provider may refuse to translate.
    /// </summary>
    /// <remarks>
    /// A mapping with no activity at all is in the set deliberately: the conditional branch has to survive
    /// the ORDER BY and the row has to come back. Where it lands is Postgres's NULL placement, not this
    /// app's choice, so only its presence is asserted.
    /// </remarks>
    [Fact]
    public async Task DesktopMappingGrid_SortsByItsActivityColumn()
    {
        await using var db = CreateDbContext();

        foreach (var name in new[] { "Zebra", "Apple" })
            await SeedMappingAsync(db, await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, name));
        await SeedMappingAsync(db, activityId: null);

        var page = await AssertAcceptedAsync(DesktopMappingGridRoute, "activity.text");

        var names = page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("activity").ValueKind == JsonValueKind.Null
                ? null
                : item.GetProperty("activity").GetProperty("text").GetString())
            .ToArray();

        names.Should().HaveCount(3, "the mapping with no activity is ordered, not filtered out");
        names.Where(n => n is not null).Should().Equal("Apple", "Zebra");

        await AssertRejectedAsync(DesktopMappingGridRoute, "activity.zzzNotAColumn");
    }

    private async Task SeedMappingAsync(DbContext db, long? activityId)
    {
        db.Set<TrackerDesktopMappingByPattern>().Add(new TrackerDesktopMappingByPattern
        {
            UserId = UserId,
            ProcessName = $"proc-{Guid.NewGuid():N}.exe",
            // The check constraint pairs each pattern column with its match type — a pattern without one is
            // rejected at the database, not at the endpoint.
            ProcessNameMatchType = PatternMatchType.Exact,
            IsActive = true,
            ActivityId = activityId,
            IsIgnored = activityId is null ? true : null
        });
        await db.SaveChangesAsync(CancellationToken);
    }
}
