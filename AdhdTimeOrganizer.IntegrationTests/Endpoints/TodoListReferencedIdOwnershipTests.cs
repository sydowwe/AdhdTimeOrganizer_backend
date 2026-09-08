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
/// <c>SEC-1</c>, <c>SEC-2</c>, <c>SEC-3</c>, <c>CQ-6</c> and <c>CQ-7</c>
/// (<c>review/todo-lists/02-findings.md</c>) — the ids a to-do write carries but does not mint, and the
/// column that has to follow one of them.
/// </summary>
/// <remarks>
/// <para>
/// The per-user query filter scopes <b>reads</b>. Copying a raw <c>long</c> out of a request into an FK
/// column is a <b>write</b>: the target row exists, so the constraint is satisfied and the row lands
/// owned by the caller but attached to somebody else's list, rank or activity. It then breaks on the way
/// back out — <c>TodoListItemResponse.Projection</c> reaches through the <b>required</b>
/// <c>TaskPriority</c> navigation, which the same filter hides — so the user's own list is corrupted by a
/// value they were allowed to send. Hence the row assertions beside every status code.
/// </para>
/// <para>
/// The guard is <c>BasePatchEndpoint.BeforeMapping</c> (added in the framework for <c>CQ-7</c>, so
/// <c>MoveTodoListItemEndpoint</c> could stop re-implementing <c>HandleAsync</c>) and
/// <c>BaseCreateEndpoint.BeforeMapping</c>, both answering 404 through
/// <c>CallerVisibleReferenceExtensions</c>. 404 rather than 403, and a foreign id indistinguishable from
/// a missing one, for the same existence-oracle reason as <see cref="PlannerReferencedIdOwnershipTests"/>
/// and <see cref="ActivityIdOwnershipTests"/>.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TodoListReferencedIdOwnershipTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const string ItemRoute = "/api/todo-list-item";

    private long _listAId;
    private long _listBId;
    private long _priority1Id;
    private long _priority2Id;
    private long _activityId;
    private long _otherActivityId;

    protected override async Task SeedAsync(DbContext db)
    {
        var role = new ActivityRole { UserId = UserId, Name = "Ownership Role", Color = "#112233" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = "Own work", RoleId = role.Id };
        var otherActivity = new Activity { UserId = UserId, Name = "Own other work", RoleId = role.Id };
        db.Set<Activity>().AddRange(activity, otherActivity);
        await db.SaveChangesAsync(CancellationToken);

        _activityId = activity.Id;
        _otherActivityId = otherActivity.Id;

        _listAId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "List A", ct: CancellationToken);
        _listBId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "List B", ct: CancellationToken);
        _priority1Id = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, UserId, ct: CancellationToken);
        _priority2Id = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 2, UserId, ct: CancellationToken);
    }

    // ---- SEC-1: the create surface -----------------------------------------------------------------

    [Fact(DisplayName = "SEC-1: creating a to-do item on another user's list is rejected")]
    public async Task CreateItem_WithForeignTodoListId_IsRejected()
    {
        var foreign = await SeedForeignFixtureAsync("foreign-create-list");

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            ItemRoute, CreateBody(todoListId: foreign.TodoListId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a list the caller does not own must read as one that does not exist");
        (await CountItemsAsync(i => i.TodoListId == foreign.TodoListId)).Should().Be(0);
    }

    [Fact(DisplayName = "SEC-1: creating a to-do item against another user's priority is rejected")]
    public async Task CreateItem_WithForeignTaskPriorityId_IsRejected()
    {
        var foreign = await SeedForeignFixtureAsync("foreign-create-priority");

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            ItemRoute, CreateBody(taskPriorityId: foreign.TaskPriorityId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "TaskPriorityId is checked independently of TodoListId — a case that only checks the list is a silent partial regression");
        (await CountItemsAsync(i => i.TaskPriorityId == foreign.TaskPriorityId)).Should().Be(0);
    }

    /// <summary>
    /// The paired activity is the third id on this request the caller does not mint. Unlike the other two
    /// it is optional and nullable, so the guard has to skip a null rather than reject it — the control
    /// below covers that half.
    /// </summary>
    [Fact(DisplayName = "SEC-1: pairing a to-do item with another user's activity is rejected")]
    public async Task CreateItem_WithForeignPairedLeisureActivityId_IsRejected()
    {
        var foreign = await SeedForeignFixtureAsync("foreign-create-paired");

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            ItemRoute, CreateBody(pairedLeisureActivityId: foreign.ActivityId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountItemsAsync(i => i.PairedLeisureActivityId == foreign.ActivityId)).Should().Be(0);
    }

    /// <summary>The control — without it every case above would pass against an endpoint hard-wired to 404.</summary>
    [Fact(DisplayName = "SEC-1: creating a to-do item from the caller's own ids still succeeds")]
    public async Task CreateItem_WithOwnIds_Succeeds()
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(
            ItemRoute, CreateBody(pairedLeisureActivityId: _otherActivityId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CountItemsAsync(i => i.TodoListId == _listAId && i.TaskPriorityId == _priority1Id)).Should().Be(1);
    }

    // ---- SEC-1: the update surface -----------------------------------------------------------------

    /// <summary>
    /// The more dangerous of the two write paths: it re-points an item the caller legitimately owns
    /// without creating anything, so there is no new row for anybody to notice. The PUT carries no
    /// <c>TodoListId</c> — moving between lists is the move patch — so the pair here is the rank and
    /// the paired activity.
    /// </summary>
    [Fact(DisplayName = "SEC-1: re-pointing a to-do item onto another user's priority is rejected")]
    public async Task UpdateItem_WithForeignTaskPriorityId_IsRejected()
    {
        var foreign = await SeedForeignFixtureAsync("foreign-put-priority");
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"{ItemRoute}/{itemId}", UpdateBody(taskPriorityId: foreign.TaskPriorityId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var after = await ReadItemAsync(itemId);
        after.TaskPriorityId.Should().Be(_priority1Id,
            "the guard runs before the row is even loaded, so a rejected PUT writes nothing at all");
    }

    [Fact(DisplayName = "SEC-1: re-pointing a to-do item's pairing at another user's activity is rejected")]
    public async Task UpdateItem_WithForeignPairedLeisureActivityId_IsRejected()
    {
        var foreign = await SeedForeignFixtureAsync("foreign-put-paired");
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"{ItemRoute}/{itemId}", UpdateBody(pairedLeisureActivityId: foreign.ActivityId), JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadItemAsync(itemId)).PairedLeisureActivityId.Should().BeNull();
    }

    /// <summary>
    /// The control. It also covers the null case the two negatives cannot: `PairedLeisureActivityId` is
    /// optional, so a guard that rejected an unset id would break every ordinary edit.
    /// </summary>
    [Fact(DisplayName = "SEC-1: updating a to-do item with the caller's own ids still succeeds")]
    public async Task UpdateItem_WithOwnIds_Succeeds()
    {
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"{ItemRoute}/{itemId}",
            UpdateBody(taskPriorityId: _priority2Id, pairedLeisureActivityId: _otherActivityId),
            JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await ReadItemAsync(itemId);
        after.TaskPriorityId.Should().Be(_priority2Id);
        after.PairedLeisureActivityId.Should().Be(_otherActivityId);
    }

    // ---- SEC-2 / CQ-6: the change-priority patch ---------------------------------------------------

    [Fact(DisplayName = "SEC-2: moving a to-do item onto another user's priority is rejected")]
    public async Task ChangePriority_WithForeignPriorityId_IsRejected()
    {
        var foreign = await SeedForeignFixtureAsync("foreign-patch-priority");
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);

        var response = await CreateUserRoleClient().PatchAsJsonAsync(
            $"{ItemRoute}/{itemId}/priority", new { priorityId = foreign.TaskPriorityId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var after = await ReadItemAsync(itemId);
        after.TaskPriorityId.Should().Be(_priority1Id,
            "the guard runs before the mapping, so a rejected patch writes nothing at all");
    }

    /// <summary>
    /// <c>CQ-6</c>. <c>DisplayOrder</c> is scoped <em>by</em> <c>TaskPriorityId</c>, so an item that
    /// changes rank keeping an order minted for the group it left lands in the middle of the new one.
    /// Asserted against the destination group's minimum rather than a literal, because the gap and the
    /// start are configuration.
    /// </summary>
    [Fact(DisplayName = "CQ-6: changing priority re-orders the item into the destination group")]
    public async Task ChangePriority_WithOwnPriorityId_RecomputesDisplayOrder()
    {
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);
        var occupantId = await SeedOwnItemAsync(_priority2Id, _listAId, _otherActivityId);

        var occupantOrder = (await ReadItemAsync(occupantId)).DisplayOrder;
        (await ReadItemAsync(itemId)).DisplayOrder.Should().Be(occupantOrder,
            "the seeded pair starts on the same order — the point is that the two groups do not share a scale");

        var response = await CreateUserRoleClient().PatchAsJsonAsync(
            $"{ItemRoute}/{itemId}/priority", new { priorityId = _priority2Id }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await ReadItemAsync(itemId);
        after.TaskPriorityId.Should().Be(_priority2Id);
        after.DisplayOrder.Should().BeLessThan(occupantOrder,
            "the item lands at the end of the group it moved into, not wherever its old order happens to fall");
    }

    // ---- SEC-3 / CQ-7: the move patch --------------------------------------------------------------

    /// <summary>
    /// The check <c>MoveTodoListItemEndpoint</c> had before <c>CQ-7</c> collapsed it onto
    /// <c>BasePatchEndpoint</c> — this is the regression pin that the collapse kept it.
    /// </summary>
    [Fact(DisplayName = "SEC-3: moving a to-do item onto another user's list is rejected")]
    public async Task MoveItem_WithForeignDestinationListId_IsRejected()
    {
        var foreign = await SeedForeignFixtureAsync("foreign-move-list");
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);

        var response = await CreateUserRoleClient().PatchAsJsonAsync(
            $"{ItemRoute}/{itemId}/move", new { destinationListId = foreign.TodoListId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadItemAsync(itemId)).TodoListId.Should().Be(_listAId);
    }

    [Fact(DisplayName = "CQ-7: moving a to-do item to the caller's own list still succeeds and re-orders it")]
    public async Task MoveItem_ToOwnList_SucceedsAndRecomputesDisplayOrder()
    {
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);
        var occupantId = await SeedOwnItemAsync(_priority1Id, _listBId, _otherActivityId);

        var occupantOrder = (await ReadItemAsync(occupantId)).DisplayOrder;

        var response = await CreateUserRoleClient().PatchAsJsonAsync(
            $"{ItemRoute}/{itemId}/move", new { destinationListId = _listBId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await ReadItemAsync(itemId);
        after.TodoListId.Should().Be(_listBId);
        after.DisplayOrder.Should().BeLessThan(occupantOrder,
            "the destination list's own ordering is what the moved item joins");
    }

    /// <summary>
    /// The reason the old <c>HandleAsync</c> override carried a copied <c>DbUtils.HandleException</c>
    /// block: the unique index on <c>(UserId, ActivityId, TodoListId)</c> is checked immediately, and a
    /// move onto a list that already holds this activity has to answer the same 409 the rest of the API
    /// gives — not the unmapped 500 an endpoint without the mapping would. Now inherited rather than
    /// copied, which is exactly what this pins.
    /// </summary>
    [Fact(DisplayName = "CQ-7: a move that collides on the unique index still answers 409, not 500")]
    public async Task MoveItem_OntoAListAlreadyHoldingTheActivity_IsConflict()
    {
        var itemId = await SeedOwnItemAsync(_priority1Id, _listAId, _activityId);
        await SeedOwnItemAsync(_priority1Id, _listBId, _activityId);

        var response = await CreateUserRoleClient().PatchAsJsonAsync(
            $"{ItemRoute}/{itemId}/move", new { destinationListId = _listBId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadItemAsync(itemId)).TodoListId.Should().Be(_listAId);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private object CreateBody(long? todoListId = null, long? taskPriorityId = null, long? pairedLeisureActivityId = null) => new
    {
        activityId = _activityId,
        taskPriorityId = taskPriorityId ?? _priority1Id,
        todoListId = todoListId ?? _listAId,
        pairedLeisureActivityId
    };

    private object UpdateBody(long? taskPriorityId = null, long? pairedLeisureActivityId = null) => new
    {
        activityId = _activityId,
        taskPriorityId = taskPriorityId ?? _priority1Id,
        isDone = false,
        displayOrder = 0,
        pairedLeisureActivityId
    };

    private async Task<long> SeedOwnItemAsync(long priorityId, long listId, long activityId)
    {
        await using var db = CreateDbContext();
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, priorityId, listId, UserId, ct: CancellationToken);
    }

    private async Task<TodoListItem> ReadItemAsync(long id)
    {
        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.Id == id, CancellationToken);
    }

    private async Task<int> CountItemsAsync(System.Linq.Expressions.Expression<Func<TodoListItem, bool>> predicate)
    {
        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().IgnoreQueryFilters().CountAsync(predicate, CancellationToken);
    }

    private async Task<(long UserId, long TodoListId, long TaskPriorityId, long ActivityId)> SeedForeignFixtureAsync(string name)
    {
        var foreignUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"todo-ownership-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, foreignUserId, CancellationToken);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, foreignUserId, $"{name}-list", ct: CancellationToken);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, foreignUserId, ct: CancellationToken);

        return (foreignUserId, listId, priorityId, activityId);
    }
}
