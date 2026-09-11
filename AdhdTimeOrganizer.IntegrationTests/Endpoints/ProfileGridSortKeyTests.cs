using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the sort keys the ActivityProfiles grids are actually sent, as opposed to the ones their responses
/// happen to expose.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this exists to stop.</b> <c>BaseGridEndpoint</c> orders the <em>projected response</em>,
/// and <c>SortByMany</c> throws <c>ArgumentException</c> on a key that resolves to no member — so a client
/// column whose key does not match a response member is a <b>500 on every table-mode render</b>, while the
/// same page in board mode (<c>sortBy: []</c>) succeeds through the <c>OrderBy(Id)</c> branch. All four of
/// these grids shipped that way: three sent <c>activity.name</c> against responses that flattened the name
/// to <c>activityName</c>, and the anchor grid sent one <c>periodKey</c> against a response that stores the
/// period as two members. Nothing in a build or a log catches it; the endpoints compile, the routes
/// resolve, and only the ordering half of the request is wrong.
/// </para>
/// <para>
/// <b>Three of the four are fixed by shape rather than by a remap.</b> The responses now nest the whole
/// <c>ActivityResponse</c>, so <c>activity.name</c> — and <c>activity.role.name</c> and
/// <c>activity.category.name</c> with it — resolve natively. <c>periodKey</c> is the one case a remap is
/// genuinely for: one client column that is two members on the response, which no shape can fix.
/// </para>
/// <para>
/// Asserted on row <b>order</b>, not on a 200, for the reason
/// <see cref="Modules.CrossSliceFilterSortTests"/> spells out: a key that lands on the wrong member still
/// returns a valid page, and on a paginated grid a wrong order hides rows entirely.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ProfileGridSortKeyTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private const string BacklogGridRoute = "api/activity-backlog-profile/grid";
    private const string BucketListGridRoute = "api/activity-bucket-list-profile/grid";
    private const string ProjectGridRoute = "api/activity-project-profile/grid";
    private const string MemoryAnchorGridRoute = "api/memory-anchor/grid";

    /// <summary>The key all four profile grids send for their activity column.</summary>
    private const string ActivityNameKey = "activity.name";

    /// <summary>The key the anchor grid sends for its single period column.</summary>
    private const string PeriodKey = "periodKey";

    private sealed record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    private static object GridBody(object[] sortBy, object? filter = null) => new
    {
        page = 1,
        itemsPerPage = 100,
        sortBy,
        useFilter = filter is not null,
        filter
    };

    private async Task<GridResponse> PostGridAsync(string route, object body)
    {
        var response = await CreateClient().PostAsJsonAsync(route, body, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a key the grid's own column header sends must resolve on the projected response — an "
            + "unresolvable one is an ArgumentException out of SortByMany, i.e. a 500");
        return (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken))!;
    }

    private static string[] ActivityNamesOf(GridResponse page) =>
        page.Items.Select(item => item.GetProperty("activity").GetProperty("name").GetString()!).ToArray();

    // ---- A: activity.name on all four grids ------------------------------------------------------

    /// <summary>
    /// Every profile grid orders by the client's <c>activity.name</c>, which now walks straight into the
    /// nested <c>ActivityResponse</c> the projection builds. Run against all four because each response
    /// projects that object itself — there is no shared projection, so a fifth profile grid added without
    /// one would fail exactly the same way.
    /// </summary>
    /// <remarks>
    /// The anchor grid is in here deliberately: its <c>activity.name</c> column was never remapped, so it
    /// was the one still 500-ing after the first pass fixed the other three.
    /// </remarks>
    [Theory]
    [InlineData("backlog", false)]
    [InlineData("backlog", true)]
    [InlineData("bucketList", false)]
    [InlineData("bucketList", true)]
    [InlineData("project", false)]
    [InlineData("project", true)]
    [InlineData("memoryAnchor", false)]
    [InlineData("memoryAnchor", true)]
    public async Task ProfileGrid_SortsByTheNestedActivityNameKeyTheClientSends(string kind, bool isDesc)
    {
        await using var db = CreateDbContext();

        foreach (var name in new[] { "Zebra", "Mango", "Apple" })
            await SeedProfileAsync(db, kind, name);

        var page = await PostGridAsync(RouteFor(kind), GridBody([new { key = ActivityNameKey, isDesc }]));

        var expected = new[] { "Apple", "Mango", "Zebra" };
        if (isDesc)
            expected = expected.Reverse().ToArray();

        ActivityNamesOf(page).Should().Equal(expected,
            "the grid sorts the projected response, so activity.name has to resolve on it — which is what "
            + "nesting the whole ActivityResponse buys, in place of the remap onto a flattened activityName");
    }

    // ---- B: the two keys nesting adds -----------------------------------------------------------

    /// <summary>
    /// Nesting makes the activity's own classification sortable too. <c>activity.role.name</c> reaches
    /// through a required navigation; <c>activity.category.name</c> reaches through one the projection
    /// builds <b>conditionally</b> (<c>Category == null ? null : new …</c>), which <c>SortByMany</c>'s
    /// remarks flag as the case a provider may refuse to translate. Both are pinned because a response that
    /// stops translating one of them still returns 200 — in the wrong order.
    /// </summary>
    /// <remarks>
    /// Role and category names are seeded in the <em>opposite</em> order to the activity names, so a sort
    /// that silently fell back to the activity name — or to the id — produces the reverse of the expected
    /// sequence rather than a coincidental match.
    /// </remarks>
    [Theory]
    [InlineData("activity.role.name")]
    [InlineData("activity.category.name")]
    public async Task ProfileGrid_SortsByTheActivitysRoleAndCategory(string key)
    {
        await using var db = CreateDbContext();

        // Activity "Alpha" is classified "Zulu", "Zulu" is classified "Alpha".
        await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Alpha", roleName: "Zulu role", categoryName: "Zulu category");
        await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Mike", roleName: "Mike role", categoryName: "Mike category");
        await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Zulu", roleName: "Alpha role", categoryName: "Alpha category");

        var page = await PostGridAsync(BacklogGridRoute, GridBody([new { key, isDesc = false }]));

        ActivityNamesOf(page).Should().Equal(["Zulu", "Mike", "Alpha"],
            "ordering by the activity's classification must order by that column, not by the activity name "
            + "it happens to sit next to");
    }

    /// <summary>
    /// A profile whose activity has no category must not disappear or fault the query when the grid is
    /// ordered by <c>activity.category.name</c> — the conditional branch of the projection has to survive
    /// the ORDER BY, and the row has to come back.
    /// </summary>
    /// <remarks>
    /// <b>Where the uncategorised row lands is not asserted.</b> That is Postgres's default (NULLs last
    /// ascending, first descending) rather than anything this app chooses, and pinning it here would freeze
    /// a database default as a contract. Pin it separately if the app ever fixes a placement.
    /// </remarks>
    [Fact]
    public async Task ProfileGrid_SortedByCategory_StillReturnsUncategorisedRows()
    {
        await using var db = CreateDbContext();

        await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Categorised", categoryName: "Some category");
        await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Uncategorised");

        var page = await PostGridAsync(BacklogGridRoute, GridBody([new { key = "activity.category.name", isDesc = false }]));

        ActivityNamesOf(page).Should().BeEquivalentTo(["Categorised", "Uncategorised"],
            "an optional navigation sorts as a LEFT JOIN — the row with no category is ordered, not filtered out");
    }

    // ---- C: periodKey on the anchor grid ---------------------------------------------------------

    /// <summary>
    /// The anchor grid's one period column expands into year-then-month, <b>both taking the column's single
    /// direction</b>. The data is chosen so a mixed direction is visible: with year descending and month
    /// ascending the 2024 rows would come back 02 before 07, and with month alone 2023-11 would lead.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemoryAnchorGrid_SortsByPeriodKeyChronologically(bool isDesc)
    {
        await using var db = CreateDbContext();

        var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "Anchored");
        foreach (var (year, month) in new[] { (2024, 2), (2023, 11), (2024, 7) })
            await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, year, month);

        var page = await PostGridAsync(MemoryAnchorGridRoute, GridBody([new { key = PeriodKey, isDesc }]));

        var expected = new[] { (2023, 11), (2024, 2), (2024, 7) };
        if (isDesc)
            expected = expected.Reverse().ToArray();

        PeriodsOf(page).Should().Equal(expected,
            "a period is one column to the client, so both expanded members carry the one isDesc it sends — "
            + "year ascending with month descending is an ordering nothing asked for");
    }

    /// <summary>
    /// The year filter needs no second code path. Once the grid is filtered to a single year every row
    /// shares an <c>anchor_year</c>, so it contributes nothing to the ordering and month decides it —
    /// <c>ORDER BY anchor_year, anchor_month</c> over one year <em>is</em> "sorted by month". Pinned so
    /// nobody adds a filter-aware branch to <c>PreprocessSortBy</c> that produces identical rows.
    /// </summary>
    [Fact]
    public async Task MemoryAnchorGrid_FilteredToOneYear_SortsByPeriodKeyOnMonth()
    {
        await using var db = CreateDbContext();

        var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "Anchored");
        foreach (var (year, month) in new[] { (2024, 9), (2023, 5), (2024, 1), (2024, 4) })
            await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, year, month);

        var page = await PostGridAsync(MemoryAnchorGridRoute,
            GridBody([new { key = PeriodKey, isDesc = false }], new { anchorYear = 2024 }));

        PeriodsOf(page).Should().Equal([(2024, 1), (2024, 4), (2024, 9)]);
    }

    // ---- D: the anchor grid's Source column ------------------------------------------------------

    /// <summary>
    /// The anchor grid's <em>Source</em> column says which profile the anchored experience came from, which
    /// the anchor row itself cannot know — the profiles are not reachable by navigation from it. The three
    /// fields are correlated subqueries in the endpoint's projection rather than a post-process overlay, for
    /// the reason the base class spells out: the page is sorted after projecting, so a value overlaid
    /// afterwards sorts on its default.
    /// </summary>
    /// <remarks>
    /// <c>backlogIsRepeatable</c> is null rather than false when there is no backlog entry — "is it
    /// repeatable" has no answer without an entry, and a false there reads as "it is one-time".
    /// </remarks>
    [Fact]
    public async Task MemoryAnchorGrid_CarriesTheSourceProfileFlags()
    {
        await using var db = CreateDbContext();

        var (bucketActivityId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "From bucket list");
        var (backlogActivityId, _) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "From backlog");
        var unprofiledActivityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "From nowhere");

        foreach (var activityId in new[] { bucketActivityId, backlogActivityId, unprofiledActivityId })
            await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, 2025, 3);

        var page = await PostGridAsync(MemoryAnchorGridRoute, GridBody([new { key = ActivityNameKey, isDesc = false }]));

        var rows = page.Items.ToDictionary(item => item.GetProperty("activity").GetProperty("name").GetString()!);

        rows["From bucket list"].GetProperty("hasBucketList").GetBoolean().Should().BeTrue();
        rows["From bucket list"].GetProperty("hasBacklog").GetBoolean().Should().BeFalse();
        rows["From bucket list"].GetProperty("backlogIsRepeatable").ValueKind.Should().Be(JsonValueKind.Null);

        rows["From backlog"].GetProperty("hasBacklog").GetBoolean().Should().BeTrue();
        rows["From backlog"].GetProperty("hasBucketList").GetBoolean().Should().BeFalse();
        rows["From backlog"].GetProperty("backlogIsRepeatable").GetBoolean().Should().BeFalse(
            "the seed helper defaults isRepeatable to false, i.e. the anchor closed the entry");

        rows["From nowhere"].GetProperty("hasBucketList").GetBoolean().Should().BeFalse();
        rows["From nowhere"].GetProperty("hasBacklog").GetBoolean().Should().BeFalse();
    }

    private static (int year, int month)[] PeriodsOf(GridResponse page) =>
        page.Items
            .Select(item => (item.GetProperty("anchorYear").GetInt32(), item.GetProperty("anchorMonth").GetInt32()))
            .ToArray();

    private static string RouteFor(string kind) => kind switch
    {
        "backlog" => BacklogGridRoute,
        "bucketList" => BucketListGridRoute,
        "project" => ProjectGridRoute,
        _ => MemoryAnchorGridRoute
    };

    private static async Task SeedProfileAsync(DbContext db, string kind, string name)
    {
        switch (kind)
        {
            case "backlog":
                await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, name);
                break;
            case "bucketList":
                await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, name);
                break;
            case "project":
                await ActivityProfileTestSeedHelper.SeedProjectProfileAsync(db, UserId, name);
                break;
            default:
                var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, name);
                await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, 2025, 6);
                break;
        }
    }
}
