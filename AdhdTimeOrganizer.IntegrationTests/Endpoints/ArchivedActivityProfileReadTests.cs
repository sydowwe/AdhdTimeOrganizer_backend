using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// What the four ActivityProfiles read surfaces do once the activity behind a row is archived — the case the
/// grids' nested <c>ActivityResponse</c> made load-bearing and nothing was pinning.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this exists to stop.</b> A projection that walks a required navigation into a
/// query-filtered principal does not return a row with empty fields — it returns <em>no row</em>, because
/// the filter turns the join into an inner one. <c>GetGridDataAsync</c> counts off the <b>unprojected</b>
/// query and pages off the projected one, so the two disagree in exactly that case: the anchor grid answered
/// <c>itemsCount: 1</c> with <c>items: []</c>, a page the client cannot turn to, with no exception, no log
/// line and a 200. Only the shape of a projection decides it, which is why every assertion here is on rows.
/// </para>
/// <para>
/// <b>The two halves are opposite on purpose.</b> A <c>MemoryAnchor</c> carries no archived filter — an
/// anchor records something already done, and retiring the activity afterwards is the normal path — so its
/// grid and get-by-id must keep showing the row and lift the filter for the join. The three
/// <c>Activity*Profile</c> entities mirror <c>!Activity.IsArchived</c> onto themselves, so their rows
/// disappear <em>with</em> the activity, count included. Both halves are pinned: one un-hides, the other
/// hides, and a future change that makes them agree breaks a test rather than a screen.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ArchivedActivityProfileReadTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private const string BacklogGridRoute = "api/activity-backlog-profile/grid";
    private const string BucketListGridRoute = "api/activity-bucket-list-profile/grid";
    private const string ProjectGridRoute = "api/activity-project-profile/grid";
    private const string MemoryAnchorGridRoute = "api/memory-anchor/grid";

    private sealed record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    private static object GridBody(object[] sortBy) => new
    {
        page = 1,
        itemsPerPage = 100,
        sortBy,
        useFilter = false,
        filter = (object?)null
    };

    private async Task<GridResponse> PostGridAsync(string route, object[] sortBy)
    {
        var response = await CreateClient().PostAsJsonAsync(route, GridBody(sortBy), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken))!;

        page.ItemsCount.Should().Be(page.Items.Length,
            "the count comes off the unprojected query and the page off the projected one, so a projection "
            + "that drops rows reports items the caller can never reach — on any page");

        return page;
    }

    /// <summary>
    /// Archives after the rows exist, which is the order the app archives in — the profile and the anchor are
    /// created against a live activity and the activity is retired later.
    /// </summary>
    private static async Task ArchiveAsync(DbContext db, long activityId)
    {
        var activity = await db.Set<Activity>().SingleAsync(a => a.Id == activityId, TestContext.Current.CancellationToken);
        activity.IsArchived = true;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // ---- the anchor grid keeps the row -----------------------------------------------------------

    /// <summary>
    /// Asserted with an empty <c>sortBy</c> and with the client's <c>activity.name</c>, because the two take
    /// different branches — the empty one orders by Id and never touches the nested object's members, so it
    /// would still pass against a projection that sorts on a name it cannot reach.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemoryAnchorGrid_KeepsTheAnchorOfAnArchivedActivity_AndStillNamesIt(bool sorted)
    {
        await using var db = CreateDbContext();

        var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "Retired Hobby");
        await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, 2025, 4);
        await ArchiveAsync(db, activityId);

        var page = await PostGridAsync(MemoryAnchorGridRoute,
            sorted ? [new { key = "activity.name", isDesc = false }] : []);

        var row = page.Items.Should().ContainSingle(
            "an anchor outlives the archiving of its activity — that is what an anchor is for").Subject;

        row.GetProperty("activity").GetProperty("name").GetString().Should().Be("Retired Hobby",
            "the grid lifts AppQueryFilters.ActivityArchived so the join to the archived activity still "
            + "resolves; without the lift the row does not render an em dash, it vanishes");
        row.GetProperty("activity").GetProperty("isArchived").GetBoolean().Should().BeTrue(
            "the client needs to be able to mark the row, which it cannot do from a row that is missing");
        row.GetProperty("activityId").GetInt64().Should().Be(activityId);
    }

    /// <summary>
    /// The Source column reads through two subqueries over the profile sets, and both profiles mirror the
    /// archived filter onto themselves — so an anchor on an archived activity would report "from nowhere"
    /// unless those subqueries lift it too. A truthful-looking <c>false</c> is the worst possible answer
    /// here: it is indistinguishable from an activity that genuinely never had a profile.
    /// </summary>
    [Fact]
    public async Task MemoryAnchorGrid_ArchivedActivity_StillReportsWhereItCameFrom()
    {
        await using var db = CreateDbContext();

        var (activityId, _) = await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "Bouldering");

        // A backlog entry on the SAME activity, so both flags and the nullable third one are exercised by a
        // single row. The helper would file its own activity, which is not what the Source column reads.
        db.Set<ActivityBacklogProfile>().Add(new ActivityBacklogProfile
        {
            ActivityId = activityId,
            LocationTypeId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityLocationType>(db, UserId, "Bouldering Gym"),
            WeatherDependencyId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityWeatherDependency>(db, UserId, "Bouldering Indoor"),
            ExpectedCostTierId = await ActivityProfileTestSeedHelper.SeedLookupAsync<ActivityExpectedCostTier>(db, UserId, "Bouldering Cheap"),
            EnergyLevel = EnergyLevel.High,
            MinParticipants = 1,
            DurationMinutes = 90,
            IsRepeatable = true
        });
        await db.SaveChangesAsync(CancellationToken);

        await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, 2025, 5);
        await ArchiveAsync(db, activityId);

        var page = await PostGridAsync(MemoryAnchorGridRoute, []);

        var row = page.Items.Should().ContainSingle().Subject;
        row.GetProperty("hasBucketList").GetBoolean().Should().BeTrue(
            "the bucket-list entry this anchor closed still exists; the archived filter hides it from its own "
            + "grid, not from the anchor's account of where the experience came from");
        row.GetProperty("hasBacklog").GetBoolean().Should().BeTrue();
        row.GetProperty("backlogIsRepeatable").GetBoolean().Should().BeTrue(
            "the seeded entry is repeatable, so anchoring it did not close it — a null here would mean the "
            + "subquery found no backlog row at all, which is the answer the filter used to force");
    }

    /// <summary>
    /// The edit form behind a grid row. Same projection, same filtered join, so it answered 404 for every
    /// anchor whose activity had been archived — a row the grid offers and the form cannot open.
    /// </summary>
    [Fact]
    public async Task GetByIdMemoryAnchor_OpensTheAnchorOfAnArchivedActivity()
    {
        await using var db = CreateDbContext();

        var activityId = await ActivityProfileTestSeedHelper.SeedActivityAsync(db, UserId, "Retired Hobby");
        var anchorId = await ActivityProfileTestSeedHelper.SeedMemoryAnchorAsync(db, UserId, activityId, 2025, 6);
        await ArchiveAsync(db, activityId);

        var response = await CreateClient().GetAsync($"api/memory-anchor/{anchorId}", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the row exists and belongs to the caller — the 404 came from the projection's join, not from the "
            + "anchor being missing or foreign");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOpts, CancellationToken);
        body.GetProperty("activity").GetProperty("name").GetString().Should().Be("Retired Hobby");
    }

    // ---- the three profile grids drop the row, count included ------------------------------------

    /// <summary>
    /// The other half of the contract, pinned so it stays a decision. These three carry
    /// <c>AppQueryFilters.ActivityArchived</c> on the entity, so the row leaves the grid <b>and</b> the count
    /// together — which is the difference between hiding a row and the mismatch this suite's anchor cases are
    /// about. A profile grid that started lifting the filter the way the anchor grid does would also start
    /// offering rows the leisure draw refuses to draw.
    /// </summary>
    [Theory]
    [InlineData("backlog")]
    [InlineData("bucketList")]
    [InlineData("project")]
    public async Task ProfileGrid_HidesTheProfileOfAnArchivedActivity_CountIncluded(string kind)
    {
        await using var db = CreateDbContext();

        var activityId = kind switch
        {
            "backlog" => (await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, "Retired Backlog")).activityId,
            "bucketList" => (await ActivityProfileTestSeedHelper.SeedBucketListProfileAsync(db, UserId, "Retired Bucket")).activityId,
            _ => (await ActivityProfileTestSeedHelper.SeedProjectProfileAsync(db, UserId, "Retired Project")).activityId
        };

        await ArchiveAsync(db, activityId);

        var route = kind switch
        {
            "backlog" => BacklogGridRoute,
            "bucketList" => BucketListGridRoute,
            _ => ProjectGridRoute
        };

        var page = await PostGridAsync(route, [new { key = "activity.name", isDesc = false }]);

        page.Items.Should().BeEmpty("an activity and its profiles disappear together");
        page.ItemsCount.Should().Be(0, "and the count has to disappear with them, or the grid pages into nothing");
    }
}
