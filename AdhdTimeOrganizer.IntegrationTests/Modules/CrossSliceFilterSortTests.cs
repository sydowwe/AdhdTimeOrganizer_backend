using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Endpoints;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Modules;

/// <summary>
/// Pins that a slice can <b>filter and sort by columns of an entity another project owns</b> — in
/// practice always <c>Activity</c>, which lives in Core and which every slice hangs off.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not a seam question, and why it still needs pinning.</b> Core is a project reference,
/// not a seam: <c>ActivityBacklogProfile.Activity</c> is an ordinary EF navigation, so filtering and
/// sorting across it is ordinary LINQ and translates to a join. The seams exist for the direction Core
/// cannot see — <em>another slice's</em> rows. What is worth freezing here is the part that is neither
/// obvious nor uniform: <b>where the ordering is applied</b>. <c>BaseGridEndpoint</c> sorts the
/// <em>projected response</em>, not the entity, so what is sortable is decided by the response DTO's
/// shape rather than by the model — and the two answers differ.
/// </para>
/// <para>
/// <b>The two shapes, both in use.</b> A response may flatten the foreign column onto itself
/// (<c>ActivityBacklogProfileResponse.ActivityName</c>) or nest it in an object
/// (<c>ActivityResponse.Role</c>). Flattened has always worked. Nested needs a dotted sort key, which
/// <c>QueryableEntityExtensions.SortByMany</c> resolves segment by segment — before that it threw
/// <c>ArgumentException</c> out of <c>Expression.PropertyOrField</c>, i.e. a 500 on a key the grid
/// looked like it should accept.
/// </para>
/// <para>
/// Asserted on returned row <b>order</b> rather than on a 200, because the failure that matters is
/// silent: a sort that does not translate as intended still returns a valid page, and on a paginated
/// grid the wrong order means rows the caller never sees at all.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class CrossSliceFilterSortTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static long UserId => FakeLoggedUserService.TestUserId;

    private const string BacklogGridRoute = "api/activity-backlog-profile/grid";
    private const string ActivityGridRoute = "api/activity/filtered-table";

    private sealed record GridResponse(JsonElement[] Items, int ItemsCount, int PageCount);

    private static object GridBody(object[] sortBy, object? filter = null) => new
    {
        page = 1,
        itemsPerPage = 100,
        sortBy,
        useFilter = filter is not null,
        filter
    };

    private async Task<GridResponse> PostGridAsync(string route, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await CreateClient().PostAsJsonAsync(route, body, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(expected);
        return (await response.Content.ReadFromJsonAsync<GridResponse>(JsonOpts, CancellationToken))!;
    }

    private static string[] StringsOf(GridResponse page, IReadOnlyCollection<long> keepIds, string idField, string valueField) =>
        page.Items
            .Where(item => keepIds.Contains(item.GetProperty(idField).GetInt64()))
            .Select(item => item.GetProperty(valueField).GetString()!)
            .ToArray();

    // ---- A: the flattened shape — ActivityProfiles sorting on a Core column ----------------------

    /// <summary>
    /// The ActivityProfiles grid orders by <c>activityName</c>, which is <c>Activity.Name</c> denormalized
    /// into the response by the projection. This is the established pattern in this solution for showing
    /// and ordering by another project's column, and the reason it is a projection field rather than a
    /// <c>PostProcessItems</c> overlay: the base sorts after projecting, so a value filled in afterwards
    /// sorts on its default.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileGrid_SortsByAnActivityColumnOwnedByCore(bool isDesc)
    {
        await using var db = CreateDbContext();

        var prefix = $"xs-{Guid.NewGuid():N}"[..12];
        var (_, zebraId) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, $"{prefix} Zebra");
        var (_, mangoId) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, $"{prefix} Mango");
        var (_, appleId) = await ActivityProfileTestSeedHelper.SeedBacklogProfileAsync(db, UserId, $"{prefix} Apple");

        var seeded = new[] { zebraId, mangoId, appleId };

        var page = await PostGridAsync(BacklogGridRoute, GridBody([new { key = "activityName", isDesc }]));

        var names = StringsOf(page, seeded, "id", "activityName");

        var expected = new[] { $"{prefix} Apple", $"{prefix} Mango", $"{prefix} Zebra" };
        if (isDesc)
            expected = expected.Reverse().ToArray();

        names.Should().Equal(expected,
            "the grid sorts the projected response, and activityName is Activity.Name flattened onto it by "
            + "ActivityBacklogProfileResponse — a slice ordering by a column Core owns");
    }

    // ---- B: the nested shape — a dotted sort key through the response object ---------------------

    /// <summary>
    /// The activity grid orders by <c>role.name</c> — a member of the nested <c>ActivityRoleResponse</c>
    /// the projection builds, reached through a dotted sort key.
    /// </summary>
    /// <remarks>
    /// This is the shape that used to 500. <c>SortByMany</c> resolved the whole key as one member name, so
    /// <c>Expression.PropertyOrField(param, "Role.Name")</c> threw — on a grid whose rows visibly carry a
    /// role, which is exactly where a client would try to sort. Each segment is now pascalized and walked
    /// in turn.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivityGrid_SortsByANestedMemberOfTheProjectedResponse(bool isDesc)
    {
        await using var db = CreateDbContext();

        var prefix = $"xs-{Guid.NewGuid():N}"[..12];
        var thirdId = await SeedActivityWithRoleAsync(db, $"{prefix} third", $"{prefix} Charlie");
        var firstId = await SeedActivityWithRoleAsync(db, $"{prefix} first", $"{prefix} Alpha");
        var secondId = await SeedActivityWithRoleAsync(db, $"{prefix} second", $"{prefix} Bravo");

        var seeded = new[] { thirdId, firstId, secondId };

        var page = await PostGridAsync(ActivityGridRoute, GridBody([new { key = "role.name", isDesc }]));

        var ordered = page.Items
            .Where(item => seeded.Contains(item.GetProperty("id").GetInt64()))
            .Select(item => item.GetProperty("id").GetInt64())
            .ToArray();

        var expected = new[] { firstId, secondId, thirdId };
        if (isDesc)
            expected = expected.Reverse().ToArray();

        ordered.Should().Equal(expected,
            "a dotted sort key must walk into the nested response object — before SortByMany split the key "
            + "this was an ArgumentException out of Expression.PropertyOrField, i.e. a 500");
    }

    /// <summary>
    /// The filter half of the same join, for contrast: filtering happens on the <b>entity</b> query in
    /// <c>ApplyCustomFiltering</c>, so it reaches through the navigation as plain LINQ and never needed a
    /// key convention at all. Pinned next to the sort so the asymmetry stays visible — a column can be
    /// filterable and not sortable, and the two are decided in different places.
    /// </summary>
    [Fact]
    public async Task ActivityGrid_FiltersByAColumnReachedThroughANavigation()
    {
        await using var db = CreateDbContext();

        var prefix = $"xs-{Guid.NewGuid():N}"[..12];
        var wantedRole = $"{prefix} Wanted";
        var keptId = await SeedActivityWithRoleAsync(db, $"{prefix} kept", wantedRole);
        var excludedId = await SeedActivityWithRoleAsync(db, $"{prefix} excluded", $"{prefix} Other");

        var page = await PostGridAsync(ActivityGridRoute, GridBody([], new { roleName = wantedRole }));

        var ids = page.Items.Select(item => item.GetProperty("id").GetInt64()).ToArray();

        ids.Should().Contain(keptId);
        ids.Should().NotContain(excludedId,
            "roleName filters through Activity.Role in ApplyCustomFiltering, which runs on the entity query "
            + "and translates to a join — no flattening or key mapping involved");
    }

    // ---- C: the failure mode --------------------------------------------------------------------

    /// <summary>
    /// An unresolvable sort key must fail, not be ignored. A silently dropped key returns a page that looks
    /// ordered and is arbitrary, which on a paginated grid hides rows entirely — the same reasoning that
    /// puts <c>usageCount</c> in the projection rather than in <c>PostProcessItemsAsync</c>.
    /// </summary>
    /// <remarks>
    /// Asserted as "not 200" rather than as a specific status: the point is that the request does not
    /// succeed with a mis-ordered page. Which non-success code it becomes is FastEndpoints' exception
    /// handling, not a contract this test should freeze.
    /// </remarks>
    [Fact]
    public async Task ActivityGrid_UnknownSortKey_DoesNotSilentlyReturnAnUnsortedPage()
    {
        var response = await CreateClient()
            .PostAsJsonAsync(ActivityGridRoute, GridBody([new { key = "role.thisIsNotAMember", isDesc = false }]), JsonOpts, CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "an ignored sort key is worse than a failed request: the caller gets a plausible page in an "
            + "order nothing chose, and never learns which rows it did not see");
    }

    private async Task<long> SeedActivityWithRoleAsync(DbContext db, string activityName, string roleName)
    {
        var role = new ActivityRole { UserId = UserId, Name = roleName, Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = activityName, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }
}
