using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>CQ-2</c>, <c>CQ-3</c> and <c>PERF-6</c> (<c>review/todo-lists/02-findings.md</c>) — the two
/// defects in <c>TodoListExtensions.GetNextDisplayOrder</c>'s single <c>MIN</c> projection.
/// </summary>
/// <remarks>
/// <para>
/// Both are silent: nothing throws at the boundary the finding describes, the group's ordering simply
/// stops being descending, so the assertions are on the minted <c>DisplayOrder</c> of a row, not on the
/// status code. The 201 alone would pass against either bug.
/// </para>
/// <para>
/// Neither case pins a literal. <c>DisplayOrderGap</c> — and <c>DisplayOrderStart</c>, derived from it
/// and <c>MaxItems</c> — are configuration, so a test asserting <c>100_000_000</c> would fail on an
/// <c>appsettings</c> edit rather than on a regression. What is invariant is that a new item lands
/// <em>below</em> everything already in its priority group.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TodoListDisplayOrderMintingTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string ItemRoute = "/api/todo-list-item";

    /// <summary>Past <c>int.MaxValue</c>, which is the whole point of <c>CQ-3</c>.</summary>
    private const long BeyondIntRange = 5_000_000_000L;

    private long _listId;
    private long _priorityId;
    private long _seededActivityId;
    private long _newActivityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var role = new ActivityRole { UserId = UserId, Name = "Display Order Role", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        // Two activities: (UserId, ActivityId, TodoListId) is unique, so the item the test creates
        // through the API cannot reuse the seeded one's activity.
        var seeded = new Activity { UserId = UserId, Name = "Seeded work", RoleId = role.Id };
        var created = new Activity { UserId = UserId, Name = "Created work", RoleId = role.Id };
        db.Set<Activity>().AddRange(seeded, created);
        await db.SaveChangesAsync(CancellationToken);

        _seededActivityId = seeded.Id;
        _newActivityId = created.Id;

        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Ordering list", ct: CancellationToken);
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, UserId, ct: CancellationToken);
    }

    /// <summary>
    /// <c>CQ-2</c>. <c>0</c> is a legal <c>DisplayOrder</c> — with the shipped configuration the 101st
    /// item in a group lands on exactly it — so a <c>!= 0</c> sentinel over a <c>?? 0</c> default cannot
    /// tell "this group is empty" from "this group's lowest item sits at zero". Under the old code the
    /// item created here was handed <c>DisplayOrderStart</c> and jumped back to the top of the group,
    /// colliding with whatever was already there; the group's order scrambles from that point on.
    /// </summary>
    [Fact(DisplayName = "CQ-2: a group whose lowest item sits at DisplayOrder 0 is not treated as empty")]
    public async Task CreateItem_WhenGroupMinimumIsZero_ContinuesDescending()
    {
        await SeedItemAsync(displayOrder: 0);

        var order = await CreateItemAndReadOrderAsync();

        order.Should().BeLessThan(0,
            "the group is not empty — the new item continues below its minimum instead of restarting at DisplayOrderStart");
    }

    /// <summary>
    /// <c>CQ-3</c> / <c>PERF-6</c>. <c>DisplayOrder</c> is a <c>bigint</c>; the projection cast it to
    /// <c>int</c>, which Postgres evaluates as <c>CAST(display_order AS integer)</c>. One row outside
    /// <c>int</c> range — reached by ordinary use after roughly 2,200 items in a group, or immediately if
    /// <c>DisplayOrderGap</c> is raised — and every creation in that group fails with
    /// <c>integer out of range</c>. (The same cast is why the projection is not sargable, which is
    /// <c>PERF-6</c>; that half is not observable from a test.)
    /// </summary>
    [Fact(DisplayName = "CQ-3: a group holding a DisplayOrder outside int range still accepts new items")]
    public async Task CreateItem_WhenGroupHoldsOrderBeyondIntRange_Succeeds()
    {
        await SeedItemAsync(displayOrder: BeyondIntRange);

        var order = await CreateItemAndReadOrderAsync();

        order.Should().BeLessThan(BeyondIntRange,
            "the MIN is taken over the bigint column, so the existing row is the group's minimum rather than an overflow");
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task SeedItemAsync(long displayOrder)
    {
        await using var db = CreateDbContext();
        await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, _seededActivityId, _priorityId, _listId, UserId,
            displayOrder: displayOrder, ct: CancellationToken);
    }

    private async Task<long> CreateItemAndReadOrderAsync()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            ItemRoute,
            new { activityId = _newActivityId, taskPriorityId = _priorityId, todoListId = _listId },
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.ActivityId == _newActivityId)
            .Select(i => i.DisplayOrder)
            .FirstAsync(CancellationToken);
    }
}
