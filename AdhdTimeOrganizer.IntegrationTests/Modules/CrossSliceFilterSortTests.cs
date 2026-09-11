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
/// <b>Nested is the shape the portal settled on.</b> A response could flatten the foreign column onto
/// itself (<c>activityName</c>) or nest it in an object (<c>ActivityResponse.Role</c>). The profile grids
/// flattened it and have been converted, so every grid that shows an activity now nests the whole
/// <c>ActivityResponse</c> — which is what makes the activity's own role and category sortable rather than
/// just its name. Nested needs a dotted sort key, which <c>QueryableEntityExtensions.SortByMany</c>
/// resolves segment by segment — before that it threw <c>ArgumentException</c> out of
/// <c>Expression.PropertyOrField</c>, i.e. a 500 on a key the grid looked like it should accept.
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
    private const string ActivityGridRoute = "api/activity/grid";

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

    // ---- A: a slice ordering by a column Core owns -----------------------------------------------

    /// <summary>
    /// The ActivityProfiles grid orders by <c>activity.name</c> — a column Core owns, reached through the
    /// <c>ActivityResponse</c> the slice's projection nests. The reason it is in the projection at all
    /// rather than a <c>PostProcessItems</c> overlay: the base sorts after projecting, so a value filled in
    /// afterwards sorts on its default.
    /// </summary>
    /// <remarks>
    /// Kept distinct from <see cref="ActivityGrid_SortsByANestedMemberOfTheProjectedResponse"/> below even
    /// though both now send a dotted key: that one is Core ordering by its own nested object, this one is a
    /// slice ordering by an entity it does not own, and the project reference that makes it work is the
    /// thing being frozen.
    /// </remarks>
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

        var page = await PostGridAsync(BacklogGridRoute, GridBody([new { key = "activity.name", isDesc }]));

        var names = page.Items
            .Where(item => seeded.Contains(item.GetProperty("id").GetInt64()))
            .Select(item => item.GetProperty("activity").GetProperty("name").GetString()!)
            .ToArray();

        var expected = new[] { $"{prefix} Apple", $"{prefix} Mango", $"{prefix} Zebra" };
        if (isDesc)
            expected = expected.Reverse().ToArray();

        names.Should().Equal(expected,
            "the grid sorts the projected response, and Activity.Name reaches it through the nested "
            + "ActivityResponse ActivityBacklogProfileResponse projects — a slice ordering by a column Core owns");
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

    /// <summary>
    /// The same dotted key, but through a member the projection builds <b>conditionally</b> —
    /// <c>ActivityResponse.Category</c> is <c>e.Category == null ? null : new ActivityCategoryResponse{…}</c>,
    /// because the FK is optional. <c>SortByMany</c>'s own remarks flag this as the case the provider may
    /// refuse, so it is pinned rather than assumed: every nested-activity response in the solution
    /// (history rows, to-do items, planner tasks, timer presets, the profile grids) carries a category in
    /// exactly this shape, and whether it is sortable decides whether they need a flattened proxy member.
    /// </summary>
    /// <remarks>
    /// Null ordering is <b>not</b> asserted. The uncategorised row is excluded from the comparison because
    /// where it lands is Postgres's default (last ascending, first descending), not something this layer
    /// chooses — see <c>SortByMany</c>. Pin that separately if the app ever fixes a placement.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivityGrid_SortsByANestedMemberOfAConditionallyProjectedObject(bool isDesc)
    {
        await using var db = CreateDbContext();

        var prefix = $"xs-{Guid.NewGuid():N}"[..12];
        var thirdId = await SeedActivityWithCategoryAsync(db, $"{prefix} third", $"{prefix} Charlie");
        var firstId = await SeedActivityWithCategoryAsync(db, $"{prefix} first", $"{prefix} Alpha");
        var secondId = await SeedActivityWithCategoryAsync(db, $"{prefix} second", $"{prefix} Bravo");

        // Uncategorised, so the ORDER BY has to survive a NULL on the conditional branch.
        await SeedActivityWithRoleAsync(db, $"{prefix} none", $"{prefix} Role");

        var seeded = new[] { thirdId, firstId, secondId };

        var page = await PostGridAsync(ActivityGridRoute, GridBody([new { key = "category.name", isDesc }]));

        var ordered = page.Items
            .Where(item => seeded.Contains(item.GetProperty("id").GetInt64()))
            .Select(item => item.GetProperty("id").GetInt64())
            .ToArray();

        var expected = new[] { firstId, secondId, thirdId };
        if (isDesc)
            expected = expected.Reverse().ToArray();

        ordered.Should().Equal(expected,
            "a conditionally projected nested object must still be orderable through — otherwise every "
            + "response carrying an optional Category needs a flattened proxy member to sort on");
    }

    private async Task<long> SeedActivityWithCategoryAsync(DbContext db, string activityName, string categoryName)
    {
        var role = new ActivityRole { UserId = UserId, Name = $"{categoryName} role", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);

        var category = new ActivityCategory { UserId = UserId, Name = categoryName, Color = "#445566" };
        db.Set<ActivityCategory>().Add(category);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = activityName, RoleId = role.Id, CategoryId = category.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
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