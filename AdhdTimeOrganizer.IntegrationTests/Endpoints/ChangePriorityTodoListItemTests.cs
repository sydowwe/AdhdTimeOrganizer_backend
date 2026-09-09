using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
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
/// <c>TEST-2</c> (<c>review/todo-lists/01-testing.md</c>) — <c>PATCH /api/todo-list-item/{id}/priority</c>,
/// which had zero tests. The route writes two columns, and the second one is invisible from the status code.
/// </summary>
/// <remarks>
/// <para>
/// <c>TaskPriorityId</c> is not a label: it is the grouping key <c>DisplayOrder</c> is minted and reordered
/// within (<c>ChangeDisplayOrderTodoListItemEndpoint.GroupFilterExpression</c>). Changing it is therefore a
/// move between ordering groups, and an order minted for the group the item just left means nothing in the
/// group it arrived in — the item silently wedges itself into the middle of the destination list
/// (<c>CQ-6</c>). Every assertion here is on rows or on the order the read endpoint returns, never on the
/// 204 alone, because the 204 was already correct while the column was wrong.
/// </para>
/// <para>
/// The other half is <c>SEC-2</c>: the validator checks the id's magnitude only and the FK is satisfied by
/// <em>any</em> user's rank, so nothing but <c>BeforeMapping</c> stands between the body and a cross-user
/// <c>TaskPriorityId</c>. Writing one is not merely untidy — <c>TodoListItemResponse.Projection</c> reads
/// <c>e.TaskPriority.Text</c> through a required navigation the per-user query filter hides, so the item
/// would vanish from its own list. The follow-on read is asserted for that reason.
/// </para>
/// <para>
/// <b>Rank band 310–319</b> — <c>task_priority</c> is unique on <c>(UserId, Priority)</c> and this
/// collection shares one database and one test user, so every file claims a band (320–329, 380–389,
/// 390–399, 700–701 are taken).
/// </para>
/// <para>
/// No concurrency scenario: <c>IPatchRequest</c> carries no <c>RowVersion</c> and
/// <c>BasePatchEndpoint</c> never reads one, so there is no stale-token path to exercise from HTTP.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ChangePriorityTodoListItemTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string ItemRoute = "/api/todo-list-item";

    private const int SourceRank = 310;
    private const int DestinationRank = 311;
    private const int ForeignRank = 312;

    private long _listId;
    private long _sourcePriorityId;
    private long _destinationPriorityId;
    private long _gap;
    private long _start;

    protected override async Task SeedAsync(DbContext db)
    {
        // DisplayOrderGap and the derived DisplayOrderStart are configuration; asserting 99_000_000 would
        // fail on an appsettings edit rather than on a regression. The relationships are the promise.
        var settings = ResolveSettings();
        _gap = settings.DisplayOrderGap;
        _start = settings.DisplayOrderStart;

        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Priority list", ct: CancellationToken);
        _sourcePriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, SourceRank, UserId, "source", CancellationToken);
        _destinationPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, DestinationRank, UserId, "destination", CancellationToken);
    }

    [Fact(DisplayName = "Patching an item's priority writes the new TaskPriorityId")]
    public async Task ChangePriority_ToAnotherOwnedPriority_Writes()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "happy", _start, _sourcePriorityId);

        var response = await ChangePriorityAsync(itemId, _destinationPriorityId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            await response.Content.ReadAsStringAsync(CancellationToken));

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.TaskPriorityId.Should().Be(_destinationPriorityId);
    }

    /// <summary>
    /// <c>CQ-6</c>. The moved item starts half a gap under the destination group's top — a value that is
    /// perfectly ordered in the group it is leaving and lands between the destination's first and second
    /// items in the one it is joining. Nothing about that is observable from the 204; only the order the
    /// user sees says which behaviour shipped.
    /// </summary>
    [Fact(DisplayName = "CQ-6: moving an item to another priority re-orders it into the destination group")]
    public async Task ChangePriority_ReMintsDisplayOrderInTheDestinationGroup()
    {
        long moved, top, middle, bottom;
        await using (var db = CreateDbContext())
        {
            moved = await SeedItemAsync(db, "moved", _start - _gap / 2, _sourcePriorityId);
            top = await SeedItemAsync(db, "dest-top", _start, _destinationPriorityId);
            middle = await SeedItemAsync(db, "dest-middle", _start - _gap, _destinationPriorityId);
            bottom = await SeedItemAsync(db, "dest-bottom", _start - 2 * _gap, _destinationPriorityId);
        }

        var response = await ChangePriorityAsync(moved, _destinationPriorityId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            await response.Content.ReadAsStringAsync(CancellationToken));

        await using var verifyDb = CreateDbContext();

        // The ordering the user would actually see, which survives any change to the arithmetic: the
        // destination group descending by DisplayOrder puts the newcomer at the end.
        var groupByOrder = await verifyDb.Set<TodoListItem>()
            .Where(i => i.TaskPriorityId == _destinationPriorityId)
            .OrderByDescending(i => i.DisplayOrder)
            .Select(i => i.Id)
            .ToListAsync(CancellationToken);

        groupByOrder.Should().Equal([top, middle, bottom, moved],
            "an item joining a group belongs at its bottom, not wherever the group it left had placed it");

        (await ReadOrderAsync(verifyDb, moved)).Should().Be(_start - 3 * _gap,
            "GetNextDisplayOrder continues one gap below the destination group's minimum");
        (await ReadOrderAsync(verifyDb, top)).Should().Be(_start, "the destination's own items are not rewritten");
        (await ReadOrderAsync(verifyDb, middle)).Should().Be(_start - _gap);
        (await ReadOrderAsync(verifyDb, bottom)).Should().Be(_start - 2 * _gap);
    }

    /// <summary>
    /// The source side of the same move. Cheap, and it is what catches a fix that buys the destination's
    /// ordering by re-spacing the group the item left.
    /// </summary>
    [Fact(DisplayName = "The vacated priority group keeps its order -- the move rewrites one row, in the destination")]
    public async Task ChangePriority_LeavesTheSourceGroupUntouched()
    {
        long moved, staysTop, staysBottom;
        await using (var db = CreateDbContext())
        {
            staysTop = await SeedItemAsync(db, "src-top", _start, _sourcePriorityId);
            moved = await SeedItemAsync(db, "src-moved", _start - _gap / 2, _sourcePriorityId);
            staysBottom = await SeedItemAsync(db, "src-bottom", _start - 2 * _gap, _sourcePriorityId);
            await SeedItemAsync(db, "dest-anchor", _start, _destinationPriorityId);
        }

        var response = await ChangePriorityAsync(moved, _destinationPriorityId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            await response.Content.ReadAsStringAsync(CancellationToken));

        await using var verifyDb = CreateDbContext();
        var remaining = await verifyDb.Set<TodoListItem>()
            .Where(i => i.TaskPriorityId == _sourcePriorityId)
            .OrderByDescending(i => i.DisplayOrder)
            .Select(i => new { i.Id, i.DisplayOrder })
            .ToListAsync(CancellationToken);

        remaining.Select(i => i.Id).Should().Equal([staysTop, staysBottom],
            "the moved item is gone from this group and the survivors keep their relative order");
        remaining.Select(i => i.DisplayOrder).Should().OnlyHaveUniqueItems(
            "two items sharing a DisplayOrder in one group is the collision the sparse scheme exists to avoid");
        (await ReadOrderAsync(verifyDb, staysTop)).Should().Be(_start);
        (await ReadOrderAsync(verifyDb, staysBottom)).Should().Be(_start - 2 * _gap);
    }

    [Fact(DisplayName = "An unknown item id is a 404")]
    public async Task ChangePriority_UnknownItem_Returns404()
    {
        var response = await ChangePriorityAsync(long.MaxValue, _destinationPriorityId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The validator is attached to the route, so a non-positive id never reaches the handler.</summary>
    [Fact(DisplayName = "PriorityId 0 is a 400 -- the validator is wired to the route")]
    public async Task ChangePriority_WithZeroPriorityId_Returns400()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "validator", _start, _sourcePriorityId);

        var response = await ChangePriorityAsync(itemId, 0);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken))
            .TaskPriorityId.Should().Be(_sourcePriorityId, "a rejected request writes nothing");
    }

    /// <summary>
    /// 404 rather than 403: the id must stay unconfirmable across accounts, the same way
    /// <c>MoveTodoListItemEndpoint</c> answers.
    /// </summary>
    [Fact(DisplayName = "IDOR: another user's item id is a 404 and their row is not written")]
    public async Task ChangePriority_ForeignItem_Returns404()
    {
        long theirItemId, theirPriorityId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var otherUserId = ReminderSeedHelper.OtherUserId;

            var theirActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"priority-theirs-{Guid.NewGuid():N}", otherUserId, CancellationToken);
            theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, ForeignRank, otherUserId, "theirs", CancellationToken);
            var theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, otherUserId, "theirs-list", ct: CancellationToken);
            theirItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, theirActivityId, theirPriorityId, theirListId, otherUserId,
                displayOrder: _start, ct: CancellationToken);
        }

        // A priority the caller does own, so the only thing that can fail is the item id.
        var response = await ChangePriorityAsync(theirItemId, _destinationPriorityId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var verifyDb = CreateDbContext();
        var theirs = await verifyDb.Set<TodoListItem>().IgnoreQueryFilters()
            .SingleAsync(i => i.Id == theirItemId, CancellationToken);
        theirs.TaskPriorityId.Should().Be(theirPriorityId, "the other user's row is not reachable from this request at all");
        theirs.DisplayOrder.Should().Be(_start);
    }

    /// <summary>
    /// <c>SEC-2</c>. The FK constraint is satisfied by another user's rank and the validator only checks the
    /// magnitude, so <c>BeforeMapping</c> is the entire ownership check on the body. Writing the foreign id
    /// would not just mislabel the item: <c>TodoListItemResponse.Projection</c> reaches
    /// <c>e.TaskPriority.Text</c> through a required navigation the query filter excludes, so the item drops
    /// out of its own list on the next read — which is why the read is asserted too.
    /// </summary>
    [Fact(DisplayName = "SEC-2: a TaskPriorityId belonging to another user is rejected, not written")]
    public async Task ChangePriority_ToForeignPriority_Returns404AndLeavesTheItemReadable()
    {
        long itemId, theirPriorityId;
        await using (var db = CreateDbContext())
        {
            itemId = await SeedItemAsync(db, "sec2", _start, _sourcePriorityId);

            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(
                db, ForeignRank + 1, ReminderSeedHelper.OtherUserId, "theirs-priority", CancellationToken);
        }

        var response = await ChangePriorityAsync(itemId, theirPriorityId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a priority the caller cannot see is not assignable, and a 403 would confirm it exists");

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken))
            .TaskPriorityId.Should().Be(_sourcePriorityId, "the rejected FK is not written");

        var items = await ReadListAsync(_listId);
        items.Select(i => i.Id).Should().Contain(itemId,
            "the item still projects through its own visible TaskPriority; a foreign one would drop the row from the read");
    }

    // ---------------------------------------------------------------- helpers

    private TodoListSettings ResolveSettings()
    {
        using var scope = Fixture.UnauthenticatedFactory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IOptions<TodoListSettings>>().Value;
    }

    private Task<HttpResponseMessage> ChangePriorityAsync(long itemId, long priorityId)
        => CreateClient().PatchAsJsonAsync($"{ItemRoute}/{itemId}/priority", new { PriorityId = priorityId },
            JsonOpts, CancellationToken);

    // (UserId, ActivityId, TodoListId) is unique, so every item gets its own activity.
    private async Task<long> SeedItemAsync(DbContext db, string name, long displayOrder, long priorityId)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"priority-{name}-{Guid.NewGuid():N}", UserId, CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, _listId, UserId,
            displayOrder: displayOrder, ct: CancellationToken);
    }

    private Task<long> ReadOrderAsync(DbContext db, long itemId)
        => db.Set<TodoListItem>().Where(i => i.Id == itemId).Select(i => i.DisplayOrder).SingleAsync(CancellationToken);

    private async Task<List<TodoListItemResponse>> ReadListAsync(long todoListId)
    {
        var response = await CreateClient().GetAsync($"{ItemRoute}?todoListId={todoListId}", CancellationToken);

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return JsonSerializer.Deserialize<List<TodoListItemResponse>>(body, JsonOpts)!;
    }
}
