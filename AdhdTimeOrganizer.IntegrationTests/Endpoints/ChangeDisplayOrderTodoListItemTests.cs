using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.infrastructure.settings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>TEST-3</c> (<c>review/todo-lists/01-testing.md</c>) — <c>PATCH /api/todo-list-item/change-display-order</c>.
/// </summary>
/// <remarks>
/// <para>
/// Before this file, no test in the repo had ever produced a <c>DisplayOrder</c> and asserted its value:
/// the route's only coverage was an IDOR 404. That is how <c>CQ-4</c> survived — the rebalance branch
/// throws <c>InvalidOperationException</c> on an untranslatable expression, and nothing had ever entered
/// it, because reaching it by hand takes ~20 successive reorders between the same pair. Everything here
/// is a 204 with an assertion on the row, not on the status.
/// </para>
/// <para>
/// The two rebalance tests are the point of the file. They are also the two that no amount of manual
/// clicking reproduces, and the ones a <c>PERF-2</c> rewrite of <c>RebalanceDisplayOrdersAsync</c> would
/// otherwise be free to break silently.
/// </para>
/// <para>
/// Expected orders are computed from <c>TodoListSettings</c> resolved out of the host, not from literals:
/// <c>DisplayOrderGap</c> is configuration and <c>DisplayOrderStart</c> is derived from it, so pinning
/// <c>99_000_000</c> would fail on an <c>appsettings</c> edit rather than on a regression. The
/// <em>relationships</em> between the numbers are what the endpoint promises.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ChangeDisplayOrderTodoListItemTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string ItemRoute = "/api/todo-list-item/change-display-order";
    private const string RoutineRoute = "/api/routine-todo-list/change-display-order";

    // TaskPriority is unique on (UserId, Priority) and the collection shares one test user. Band 320-329
    // is this file's; 200-201, 380-389, 700-701 and 900 belong to other files in this folder.
    private const int PriorityRank = 320;
    private const int OtherPriorityRank = 321;
    private const int ForeignPriorityRank = 322;

    private long _listId;
    private long _priorityId;
    private long _gap;
    private long _start;

    protected override async Task SeedAsync(DbContext db)
    {
        var settings = ResolveSettings();
        _gap = settings.DisplayOrderGap;
        _start = settings.DisplayOrderStart;

        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Reorder list", ct: CancellationToken);
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, PriorityRank, UserId, ct: CancellationToken);
    }

    // ---------------------------------------------------------------- the three arithmetic cases

    /// <summary>
    /// The ordinary drag: the moved item takes the exact midpoint of its two new neighbours, and its two
    /// neighbours are not written at all — which is the whole reason the column is sparse.
    /// </summary>
    [Fact(DisplayName = "Reordering between two neighbours lands on their exact midpoint and leaves both neighbours untouched")]
    public async Task Reorder_BetweenTwoNeighbours_TakesTheMidpoint()
    {
        long top, middle, moved;
        await using (var db = CreateDbContext())
        {
            top = await SeedItemAsync(db, "top", _start);
            middle = await SeedItemAsync(db, "middle", _start - 2 * _gap);
            moved = await SeedItemAsync(db, "moved", _start - 4 * _gap);
        }

        var response = await ReorderAsync(ItemRoute, moved, precedingItemId: middle, followingItemId: top);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        (await ReadOrderAsync(verifyDb, moved)).Should().Be(_start - _gap,
            "the midpoint of (start - 2*gap, start) is start - gap");
        (await ReadOrderAsync(verifyDb, top)).Should().Be(_start, "a reorder writes one row");
        (await ReadOrderAsync(verifyDb, middle)).Should().Be(_start - 2 * _gap, "a reorder writes one row");
    }

    /// <summary>
    /// No preceding sibling: the endpoint invents one a gap below the following item and takes the
    /// midpoint of that, so the moved item lands half a gap under the item it was dropped against.
    /// </summary>
    [Fact(DisplayName = "Reordering with no preceding sibling lands half a gap below the following item")]
    public async Task Reorder_WithNoPrecedingSibling_LandsHalfAGapBelowFollowing()
    {
        long top, moved;
        await using (var db = CreateDbContext())
        {
            top = await SeedItemAsync(db, "top", _start);
            moved = await SeedItemAsync(db, "moved", _start - 2 * _gap);
        }

        var response = await ReorderAsync(ItemRoute, moved, precedingItemId: null, followingItemId: top);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var order = await ReadOrderAsync(verifyDb, moved);
        order.Should().Be(_start - _gap / 2, "the midpoint of (F - gap, F) is F - gap/2");
        order.Should().BeLessThan(_start, "the moved item is placed relative to the item it was dropped against, not above it");
    }

    /// <summary>
    /// No following sibling: the invented neighbour sits a gap <em>above</em> the preceding item, so the
    /// moved item ends up half a gap above it rather than continuing the descent below it.
    /// </summary>
    [Fact(DisplayName = "Reordering with no following sibling lands half a gap above the preceding item")]
    public async Task Reorder_WithNoFollowingSibling_LandsHalfAGapAbovePreceding()
    {
        long bottom, moved;
        await using (var db = CreateDbContext())
        {
            bottom = await SeedItemAsync(db, "bottom", _start - 4 * _gap);
            moved = await SeedItemAsync(db, "moved", _start);
        }

        var response = await ReorderAsync(ItemRoute, moved, precedingItemId: bottom, followingItemId: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        (await ReadOrderAsync(verifyDb, moved)).Should().Be(_start - 4 * _gap + _gap / 2,
            "the midpoint of (P, P + gap) is P + gap/2");
    }

    /// <summary>
    /// Both neighbour ids null — the only item in the list is dropped on itself. This used to answer
    /// <c>DisplayOrderGap</c> where creation answers <c>DisplayOrderStart</c>, two different "first item"
    /// values for one column (the nits appendix in <c>02-findings.md</c>). The reorder route now agrees
    /// with <c>GetNextDisplayOrder</c>, and this test is what keeps them agreeing.
    /// </summary>
    [Fact(DisplayName = "Reordering with no neighbours at all yields DisplayOrderStart, the same value creation mints for a first item")]
    public async Task Reorder_WithNoNeighbours_YieldsDisplayOrderStart()
    {
        long moved;
        await using (var db = CreateDbContext())
            moved = await SeedItemAsync(db, "only", _start - 7 * _gap);

        var response = await ReorderAsync(ItemRoute, moved, precedingItemId: null, followingItemId: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        (await ReadOrderAsync(verifyDb, moved)).Should().Be(_start,
            "an empty list's first order is DisplayOrderStart on both the create and the reorder path");
    }

    // ---------------------------------------------------------------- the failure paths

    [Theory(DisplayName = "An unresolvable neighbour id 404s and leaves the moved item's order untouched")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reorder_WithUnknownNeighbourId_Returns404AndWritesNothing(bool unknownIsPreceding)
    {
        long anchor, moved;
        await using (var db = CreateDbContext())
        {
            anchor = await SeedItemAsync(db, "anchor", _start);
            moved = await SeedItemAsync(db, "moved", _start - 2 * _gap);
        }

        const long missingId = 987_654_321;
        var response = await ReorderAsync(ItemRoute, moved,
            precedingItemId: unknownIsPreceding ? missingId : anchor,
            followingItemId: unknownIsPreceding ? anchor : missingId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verifyDb = CreateDbContext();
        (await ReadOrderAsync(verifyDb, moved)).Should().Be(_start - 2 * _gap,
            "the calculation fails before the item is written, inside the transaction");
    }

    /// <summary>
    /// <c>SEC-4</c>. The moved item's own lookup passes an explicit <c>userId</c>, as every sibling lookup
    /// in the same request already did — so this is a 404 on the predicate, not only on the global query
    /// filter that an <c>IgnoreQueryFilters()</c> somewhere upstream could lift.
    /// </summary>
    [Fact(DisplayName = "SEC-4: reordering another user's item 404s and leaves their order untouched")]
    public async Task Reorder_ForeignMovedItem_Returns404()
    {
        long anchor, theirItem;
        await using (var db = CreateDbContext())
        {
            anchor = await SeedItemAsync(db, "anchor", _start);

            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var otherUserId = ReminderSeedHelper.OtherUserId;
            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"reorder-theirs-{Guid.NewGuid():N}", otherUserId, CancellationToken);
            var theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, ForeignPriorityRank, otherUserId, ct: CancellationToken);
            var theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, otherUserId, "reorder-theirs-list", ct: CancellationToken);
            theirItem = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, theirActivityId, theirPriorityId, theirListId, otherUserId,
                displayOrder: _start, ct: CancellationToken);
        }

        var response = await ReorderAsync(ItemRoute, theirItem, precedingItemId: anchor, followingItemId: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verifyDb = CreateDbContext();
        var theirs = await verifyDb.Set<TodoListItem>().IgnoreQueryFilters()
            .SingleAsync(i => i.Id == theirItem, CancellationToken);
        theirs.DisplayOrder.Should().Be(_start, "the other user's row is not reachable from this request at all");
    }

    // ---------------------------------------------------------------- the rebalance branch

    /// <summary>
    /// <c>CQ-4</c>. Two neighbours one apart have no midpoint between them, so the endpoint re-spaces the
    /// whole priority group and retries. Under the original code this answered 500 — the group predicate
    /// was built as <c>GroupFilterExpression.Compile()(e)</c>, which EF cannot translate — and the pair
    /// stayed permanently unreorderable until someone edited the rows by hand.
    /// </summary>
    [Fact(DisplayName = "CQ-4: exhausting the gap between two neighbours re-spaces the group and completes the move")]
    public async Task Reorder_WhenGapIsExhausted_RebalancesAndCompletes()
    {
        long upper, lower, moved;
        await using (var db = CreateDbContext())
        {
            // One apart: ArithmeticNewOrderNumber(lower, upper) == lower, which is the conflict trigger.
            upper = await SeedItemAsync(db, "upper", 50_000_001);
            lower = await SeedItemAsync(db, "lower", 50_000_000);
            moved = await SeedItemAsync(db, "moved", 40_000_000);
        }

        var response = await ReorderAsync(ItemRoute, moved, precedingItemId: lower, followingItemId: upper);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            $"the rebalance must translate to SQL and the retry must succeed. Body: {await response.Content.ReadAsStringAsync(CancellationToken)}");

        await using var verifyDb = CreateDbContext();
        var upperOrder = await ReadOrderAsync(verifyDb, upper);
        var lowerOrder = await ReadOrderAsync(verifyDb, lower);
        var movedOrder = await ReadOrderAsync(verifyDb, moved);

        upperOrder.Should().Be(_start - _gap, "the rebalance re-lays the group from DisplayOrderStart downwards, highest first");
        lowerOrder.Should().Be(_start - 2 * _gap, "one full gap below its neighbour");
        movedOrder.Should().BeGreaterThan(lowerOrder).And.BeLessThan(upperOrder,
            "the retry places the moved item in the gap the rebalance just opened");

        var group = await verifyDb.Set<TodoListItem>()
            .Where(i => i.TaskPriorityId == _priorityId)
            .Select(i => i.DisplayOrder)
            .ToListAsync(CancellationToken);
        group.Should().OnlyHaveUniqueItems("a rebalance that collides two items is worse than no rebalance");
    }

    /// <summary>
    /// <c>CQ-5</c>. The retry is a retry, not a loop. <c>RebalanceDisplayOrdersAsync</c> only re-spaces the
    /// <em>following</em> item's group, so a preceding item from another group can be left one away from
    /// the following item a second time — and the recursion re-enters with identical arguments. Without
    /// the <c>alreadyRebalanced</c> guard that is unbounded recursion: the process dies rather than the
    /// request failing, which is why this test asserts a status code at all. The 409 it asserts is the one
    /// the endpoint's own <c>Summary</c> block has advertised since it was written.
    /// </summary>
    [Fact(DisplayName = "CQ-5: a second exhausted gap answers 409 instead of recursing, and rolls the rebalance back")]
    public async Task Reorder_WhenRebalanceCannotOpenAGap_Returns409()
    {
        long foreignGroupPreceding, following, moved;
        await using (var db = CreateDbContext())
        {
            var otherPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, OtherPriorityRank, UserId, ct: CancellationToken);

            // The preceding item is in a different priority group, so the rebalance never touches it, and
            // it sits one above where the rebalance is going to park the following item: the top of a
            // re-spaced group is always DisplayOrderStart - DisplayOrderGap. So the retry hits the same
            // exhausted gap it started with.
            foreignGroupPreceding = await SeedItemAsync(db, "foreign", _start - _gap + 1, otherPriorityId);
            following = await SeedItemAsync(db, "following", _start - _gap);
            moved = await SeedItemAsync(db, "moved", 50_000_000);
        }

        var response = await ReorderAsync(ItemRoute, moved, precedingItemId: foreignGroupPreceding, followingItemId: following);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the second failed calculation is the 409 the Summary block promises, not a third rebalance");

        await using var verifyDb = CreateDbContext();
        (await ReadOrderAsync(verifyDb, moved)).Should().Be(50_000_000,
            "the handler returns without committing, so the rebalance's writes roll back with the transaction");
        (await ReadOrderAsync(verifyDb, following)).Should().Be(_start - _gap);
        (await ReadOrderAsync(verifyDb, foreignGroupPreceding)).Should().Be(_start - _gap + 1);
    }

    // ---------------------------------------------------------------- completion state

    /// <summary>
    /// A reorder is a <c>DisplayOrder</c> write and nothing else. Cheap to assert and worth pinning
    /// before <c>PERF-2</c>: the <c>ExecuteUpdateAsync</c> rewrite it proposes bypasses the
    /// ChangeTracker, and therefore <c>TodoListItemCompletionInterceptor</c>, which is only safe as long
    /// as this stays true.
    /// </summary>
    [Fact(DisplayName = "Reordering a completed item touches neither IsDone nor CompletedTimestamp")]
    public async Task Reorder_DoneItem_LeavesCompletionStateAlone()
    {
        long anchor, moved;
        DateTime stampedAt;
        await using (var db = CreateDbContext())
        {
            anchor = await SeedItemAsync(db, "anchor", _start);
            moved = await SeedItemAsync(db, "done", _start - 2 * _gap, isDone: true);

            // Stamp the completion explicitly, nine days back: a value no clock reaches during the run,
            // so an accidental re-stamp by TodoListItemCompletionInterceptor is unmistakable. The
            // interceptor is host-side and does not run on the fixture's DbContext, so seeding
            // IsDone = true here leaves CompletedTimestamp null — this is the write that sets it.
            stampedAt = DateTime.UtcNow.Date.AddDays(-9);
            var item = await db.Set<TodoListItem>().SingleAsync(i => i.Id == moved, CancellationToken);
            item.CompletedTimestamp = stampedAt;
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await ReorderAsync(ItemRoute, moved, precedingItemId: null, followingItemId: anchor);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var after = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == moved, CancellationToken);
        after.DisplayOrder.Should().Be(_start - _gap / 2, "the reorder itself still happened");
        after.IsDone.Should().BeTrue();
        after.CompletedTimestamp.Should().BeCloseTo(stampedAt, TimeSpan.FromSeconds(1),
            "moving an item in the list is not a completion event");
    }

    // ---------------------------------------------------------------- the Routines twin

    /// <summary>
    /// The same generic base is subclassed from <c>AdhdTimeOrganizer.Routines</c> with
    /// <c>TimePeriodId</c> as its grouping column. Without this, every fix above would be validated on
    /// exactly one of the base's two closed generic types.
    /// </summary>
    [Fact(DisplayName = "The same base reorders RoutineTodoList by TimePeriodId, midpoint and all")]
    public async Task Reorder_RoutineTodoList_TakesTheMidpoint()
    {
        long top, middle, moved;
        await using (var db = CreateDbContext())
        {
            var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, UserId, "reorder-period", ct: CancellationToken);
            top = await SeedRoutineItemAsync(db, periodId, "top", _start);
            middle = await SeedRoutineItemAsync(db, periodId, "middle", _start - 2 * _gap);
            moved = await SeedRoutineItemAsync(db, periodId, "moved", _start - 4 * _gap);
        }

        var response = await ReorderAsync(RoutineRoute, moved, precedingItemId: middle, followingItemId: top);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var order = await verifyDb.Set<RoutineTodoList>()
            .Where(i => i.Id == moved)
            .Select(i => i.DisplayOrder)
            .SingleAsync(CancellationToken);
        order.Should().Be(_start - _gap);
    }

    // ---------------------------------------------------------------- helpers

    private TodoListSettings ResolveSettings()
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<TodoListSettings>>().Value;
    }

    private Task<HttpResponseMessage> ReorderAsync(string route, long movedItemId, long? precedingItemId, long? followingItemId)
        => CreateClient().PatchAsJsonAsync(route, new
        {
            MovedItemId = movedItemId,
            PrecedingItemId = precedingItemId,
            FollowingItemId = followingItemId
        }, JsonOpts, CancellationToken);

    // (UserId, ActivityId, TodoListId) is unique, so every item gets its own activity.
    private async Task<long> SeedItemAsync(DbContext db, string name, long displayOrder, long? priorityId = null, bool isDone = false)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"reorder-{name}-{Guid.NewGuid():N}", UserId, CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId ?? _priorityId, _listId, UserId,
            isDone: isDone, displayOrder: displayOrder, ct: CancellationToken);
    }

    private async Task<long> SeedRoutineItemAsync(DbContext db, long timePeriodId, string name, long displayOrder)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"reorder-routine-{name}-{Guid.NewGuid():N}", UserId, CancellationToken);
        return await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, timePeriodId, UserId,
            displayOrder: displayOrder, ct: CancellationToken);
    }

    private async Task<long> ReadOrderAsync(DbContext db, long id)
        => await db.Set<TodoListItem>().Where(i => i.Id == id).Select(i => i.DisplayOrder).SingleAsync(CancellationToken);
}
