using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
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
/// <c>TEST-1</c> (<c>review/todo-lists/01-testing.md</c>) — <c>PATCH /api/todo-list-item/{id}/move</c>,
/// which had zero tests of any kind.
/// </summary>
/// <remarks>
/// <para>
/// The route writes two columns from one request and only one of them is in the body.
/// <c>TodoListId</c> comes from the caller; <c>DisplayOrder</c> is re-minted by
/// <c>GetNextDisplayOrder</c>'s group-filter overload so the item lands at the bottom of the list it
/// joins rather than keeping an order that only meant something in the list it left. Nothing about
/// that second write is visible from the 204, and this is the only file in the suite that asserts the
/// minted value itself rather than the resulting order — so it is the only thing standing between a
/// refactor of that overload and an item wedged into the middle of its new list.
/// </para>
/// <para>
/// The ownership half: <c>MoveToListTodoListItemValidator</c> checks the destination id's magnitude
/// and nothing else, and the FK constraint is satisfied by whoever owns the target list, so
/// <c>BeforeMapping</c>'s <c>EnsureReferenceVisibleAsync&lt;TodoList&gt;</c> is the entire guard
/// against filing a task onto a stranger's list. 404 rather than 403 on both sides, deliberately: a
/// 403 would confirm the id exists and make another account's sequential ids enumerable.
/// </para>
/// <para>
/// The 409 scenario is the one that justifies <c>BasePatchEndpoint</c>'s exception mapping.
/// <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>, so a move can collide
/// with a row the caller already holds in the destination — the only route here where a legal-looking
/// request reaches Postgres and fails there. Without this test someone simplifies the
/// <c>DbUtils.HandleException</c> mapping away and the collision starts answering 500. It must stay
/// green through any further <c>CQ-7</c>-style refactor of the endpoint onto its base.
/// </para>
/// <para>
/// <b>Rank band 360–369</b> — <c>task_priority</c> is unique on <c>(UserId, Priority)</c> and this
/// collection shares one database and one test user, so every file claims a band (310–319, 320–329,
/// 340–349, 350, 380–389, 390–399, 700–701 are taken).
/// </para>
/// </remarks>
[Collection("Postgres")]
public class MoveTodoListItemTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string ItemRoute = "/api/todo-list-item";

    private const int PriorityRank = 360;
    private const int ForeignPriorityRank = 361;

    /// <summary>An id nobody owns — the "unknown id" half of the 404 pair.</summary>
    private const long NoSuchId = 999_999_999;

    private long _listAId;
    private long _listBId;
    private long _priorityId;
    private long _gap;
    private long _start;

    protected override async Task SeedAsync(DbContext db)
    {
        // DisplayOrderGap and the derived DisplayOrderStart are configuration (appsettings.json binds
        // MaxItems: 100, DisplayOrderGap: 1000000); hard-coding 100_000_000 would fail on an
        // appsettings edit rather than on a regression. The arithmetic is the promise, not the number.
        var settings = ResolveSettings();
        _gap = settings.DisplayOrderGap;
        _start = settings.DisplayOrderStart;

        _listAId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "move-source", ct: CancellationToken);
        _listBId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "move-destination", ct: CancellationToken);
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, PriorityRank, UserId, "move", CancellationToken);
    }

    /// <summary>
    /// The whole point of the route: the FK moves and the order is re-minted one gap below the
    /// destination's lowest, not carried over from the source list. The value is asserted, not merely
    /// "it changed" — an implementation that left <c>DisplayOrder</c> alone would still pass an
    /// "it moved" assertion while putting the item wherever its old order happened to fall.
    /// </summary>
    [Fact(DisplayName = "Moving an item writes the destination list and re-mints DisplayOrder below the destination's lowest")]
    public async Task Move_ToNonEmptyList_WritesListAndReMintsDisplayOrder()
    {
        long itemId, destTop, destBottom;
        await using (var db = CreateDbContext())
        {
            itemId = await SeedItemAsync(db, "moved", _listAId, _start);
            destTop = await SeedItemAsync(db, "dest-top", _listBId, _start);
            destBottom = await SeedItemAsync(db, "dest-bottom", _listBId, _start - _gap);
        }

        var response = await MoveAsync(itemId, _listBId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            await response.Content.ReadAsStringAsync(CancellationToken));

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);

        item.TodoListId.Should().Be(_listBId);
        item.DisplayOrder.Should().Be(_start - 2 * _gap,
            "GetNextDisplayOrder continues one gap below the destination group's minimum");

        // The ordering a user would actually see, which survives any change to the arithmetic.
        var byOrder = await verifyDb.Set<TodoListItem>()
            .Where(i => i.TodoListId == _listBId)
            .OrderByDescending(i => i.DisplayOrder)
            .Select(i => i.Id)
            .ToListAsync(CancellationToken);

        byOrder.Should().Equal([destTop, destBottom, itemId],
            "an item joining a list belongs at its bottom, not wherever the list it left had placed it");
    }

    /// <summary>
    /// The empty-group branch of <c>GetNextDisplayOrder</c>: <c>MinAsync</c> over <c>long?</c> answers
    /// null and the item is seeded at <c>DisplayOrderStart</c> rather than at <c>0 - gap</c>.
    /// </summary>
    [Fact(DisplayName = "Moving an item into an empty list mints DisplayOrderStart")]
    public async Task Move_ToEmptyList_MintsDisplayOrderStart()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "moved", _listAId, _start - 5 * _gap);

        var response = await MoveAsync(itemId, _listBId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            await response.Content.ReadAsStringAsync(CancellationToken));

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);

        item.TodoListId.Should().Be(_listBId);
        item.DisplayOrder.Should().Be(_start,
            "an empty destination has no minimum to step below, so the order restarts at the top");
    }

    [Fact(DisplayName = "An unknown item id is a 404 and moves nothing")]
    public async Task Move_UnknownItem_Returns404()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "bystander", _listAId, _start);

        var response = await MoveAsync(NoSuchId, _listBId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken))
            .TodoListId.Should().Be(_listAId, "a 404 on one id must not have touched another row");
        (await verifyDb.Set<TodoListItem>().CountAsync(i => i.TodoListId == _listBId, CancellationToken))
            .Should().Be(0);
    }

    /// <summary>
    /// The destination check runs in <c>BeforeMapping</c>, ahead of the write. Asserting
    /// <c>DisplayOrder</c> as well as <c>TodoListId</c> is what catches a reordering of those two steps:
    /// a version that mapped first and validated second would leave a half-applied patch on the tracked
    /// entity for anything else on the request to commit.
    /// </summary>
    [Fact(DisplayName = "An unknown destination list id is a 404 and the item is untouched")]
    public async Task Move_UnknownDestinationList_Returns404_AndLeavesTheItemUntouched()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "untouched", _listAId, _start - _gap);

        var response = await MoveAsync(itemId, NoSuchId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unowned list id must be rejected by the visibility check, not by a Postgres FK violation");

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.TodoListId.Should().Be(_listAId);
        item.DisplayOrder.Should().Be(_start - _gap, "neither column is written when the destination is rejected");
    }

    /// <summary>
    /// The IDOR the validator cannot see: it reads a number, and the FK is satisfied by whoever owns the
    /// destination. 404 rather than 403 — every entity here is <c>IEntityWithUser</c> and the global
    /// query filter makes a foreign row simply not exist.
    /// </summary>
    [Fact(DisplayName = "IDOR: another user's list is not a valid destination, and the item stays put")]
    public async Task Move_ToForeignList_Returns404()
    {
        long itemId, theirListId;
        await using (var db = CreateDbContext())
        {
            itemId = await SeedItemAsync(db, "mine", _listAId, _start);

            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(
                db, ReminderSeedHelper.OtherUserId, "move-theirs", ct: CancellationToken);
        }

        var response = await MoveAsync(itemId, theirListId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a list the caller cannot see must be indistinguishable from one that does not exist");

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().IgnoreQueryFilters()
            .SingleAsync(i => i.Id == itemId, CancellationToken);
        item.TodoListId.Should().Be(_listAId, "the caller's task must not land on a stranger's list");
        item.DisplayOrder.Should().Be(_start);
    }

    /// <summary>
    /// The other direction, and the one that pins <em>where</em> the guard lives: the item is fetched by
    /// <c>FindAsync</c> in <c>BasePatchEndpoint.HandleAsync</c>, which returns a tracked entity without
    /// re-querying if one is already tracked. Whether the per-user filter covers that lookup is exactly
    /// what this asserts, and the answer is only observable from the foreign row afterwards — a 204 here
    /// would be a cross-account write.
    /// </summary>
    [Fact(DisplayName = "IDOR: another user's item cannot be moved, and their row is not written")]
    public async Task Move_ForeignItem_Returns404_AndTheirRowIsUnchanged()
    {
        long theirItemId, theirListId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var otherUserId = ReminderSeedHelper.OtherUserId;

            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, $"move-theirs-{Guid.NewGuid():N}", otherUserId, CancellationToken);
            var theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(
                db, ForeignPriorityRank, otherUserId, "theirs", CancellationToken);
            theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, otherUserId, "theirs-list", ct: CancellationToken);
            theirItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(
                db, theirActivityId, theirPriorityId, theirListId, otherUserId, displayOrder: _start, ct: CancellationToken);
        }

        // A destination the caller does own, so the only thing that can fail is the item id.
        var response = await MoveAsync(theirItemId, _listBId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verifyDb = CreateDbContext();
        var theirs = await verifyDb.Set<TodoListItem>().IgnoreQueryFilters()
            .SingleAsync(i => i.Id == theirItemId, CancellationToken);
        theirs.TodoListId.Should().Be(theirListId, "the other user's row is not reachable from this request at all");
        theirs.DisplayOrder.Should().Be(_start);
    }

    /// <summary>
    /// The case the base's <c>DbUtils.HandleException</c> mapping exists for, and the only one on this
    /// route where a well-formed, fully-owned request reaches Postgres and fails there:
    /// <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>, and the destination
    /// already holds the caller's own item for that activity. A 500 here is the regression.
    /// </summary>
    [Fact(DisplayName = "A move that collides with the unique (UserId, ActivityId, TodoListId) index answers 409, not 500")]
    public async Task Move_IntoUniqueIndexCollision_AnswersConflict()
    {
        long itemOnA, itemOnB;
        await using (var db = CreateDbContext())
        {
            // One activity on two lists is legal; the same activity twice on one list is not.
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(
                db, $"move-collision-{Guid.NewGuid():N}", UserId, CancellationToken);

            itemOnA = await TodoListTestSeedHelper.SeedTodoListItemAsync(
                db, activityId, _priorityId, _listAId, UserId, displayOrder: _start, ct: CancellationToken);
            itemOnB = await TodoListTestSeedHelper.SeedTodoListItemAsync(
                db, activityId, _priorityId, _listBId, UserId, displayOrder: _start, ct: CancellationToken);
        }

        var response = await MoveAsync(itemOnA, _listBId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a unique violation must map through DbUtils.HandleException, not escape as a raw 500");

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemOnA, CancellationToken))
            .TodoListId.Should().Be(_listAId, "the failed save leaves the item where it was");
        (await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemOnB, CancellationToken))
            .TodoListId.Should().Be(_listBId);
    }

    /// <summary>
    /// Covers the validator being attached to the route at all — a <c>Validator&lt;&gt;()</c> call
    /// dropped from <c>Configure</c> is exactly the kind of silent failure this suite exists to catch,
    /// since every other scenario would still pass without it.
    /// </summary>
    [Fact(DisplayName = "DestinationListId 0 is a 400 -- the validator is wired to the route")]
    public async Task Move_WithZeroDestinationListId_Returns400()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "validator", _listAId, _start);

        var response = await MoveAsync(itemId, 0);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.TodoListId.Should().Be(_listAId, "a rejected request writes nothing");
        item.DisplayOrder.Should().Be(_start);
    }

    // ---------------------------------------------------------------- helpers

    private TodoListSettings ResolveSettings()
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<TodoListSettings>>().Value;
    }

    private Task<HttpResponseMessage> MoveAsync(long itemId, long destinationListId)
        => CreateClient().PatchAsJsonAsync($"{ItemRoute}/{itemId}/move", new { DestinationListId = destinationListId },
            JsonOpts, CancellationToken);

    // (UserId, ActivityId, TodoListId) is unique, so every item gets its own activity unless the
    // collision is the thing under test.
    private async Task<long> SeedItemAsync(DbContext db, string name, long todoListId, long displayOrder)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"move-{name}-{Guid.NewGuid():N}", UserId, CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, _priorityId, todoListId, UserId,
            displayOrder: displayOrder, ct: CancellationToken);
    }
}
