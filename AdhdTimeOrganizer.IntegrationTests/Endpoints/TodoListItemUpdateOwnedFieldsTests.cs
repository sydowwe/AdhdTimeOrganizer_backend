using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>SEC-5</c>, <c>CQ-16</c> and <c>CQ-19</c> (<c>review/todo-lists/02-findings.md</c>) — what
/// <c>PUT /api/todo-list-item/{id}</c> owns and what it must leave alone.
/// </summary>
/// <remarks>
/// <para>
/// All three are silent. The PUT returned 200 in every case below; the damage was in the row — a step
/// identity replaced, a display order overwritten, an <c>IsDone</c> that agreed with neither
/// <c>DoneCount</c> nor the steps. So every assertion here is on persisted state, and the status code is
/// only checked to prove the request was not rejected for some unrelated reason.
/// </para>
/// <para>
/// The bodies deliberately post <c>displayOrder</c> and <c>doneCount</c> even though
/// <c>BaseUpdateTodoListRequest</c> no longer has either field. Unknown properties are ignored on
/// binding, so this is what a stale client — or the old frontend, echoing back what it read — actually
/// sends, and it is the shape under which the endpoint has to hold the line. Re-adding either property
/// to the request record makes these fail.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class TodoListItemUpdateOwnedFieldsTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private long _listId;
    private long _priorityId;
    private long _activityId;

    protected override async Task SeedAsync(DbContext db)
    {
        _activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "put-owned-fields", ct: CancellationToken);
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 910, UserId, text: "put-priority", ct: CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "put-list", ct: CancellationToken);
    }

    // ---- CQ-19: the step Guid is a public key -------------------------------------------------------

    /// <summary>
    /// <c>CQ-19</c>. The steps were rebuilt from fresh <see cref="TodoListStep"/> instances, and
    /// <c>Id</c> defaults to <c>Guid.NewGuid()</c> on a <c>ValueGeneratedNever()</c> column — so every
    /// PUT minted a new id for every step. Three routes take that id in the path, so after an unrelated
    /// note edit the client's step ids all 404 against steps that are visibly on screen.
    /// </summary>
    [Fact(DisplayName = "CQ-19: a PUT keeps each step's Guid, so the step routes still resolve afterwards")]
    public async Task UpdateItem_PreservesStepIds_AndTheStepRoutesStillWork()
    {
        var keptId = Guid.NewGuid();
        var alsoKeptId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, steps:
            [
                new TodoListStep { Id = keptId, Name = "first", Order = 0 },
                new TodoListStep { Id = alsoKeptId, Name = "second", Order = 1 }
            ]);

        var client = CreateUserRoleClient();
        var response = await client.PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            note: "an edit that has nothing to do with the checklist",
            steps:
            [
                new { id = keptId, name = "first", order = 0 },
                new { id = alsoKeptId, name = "second, renamed", order = 1 }
            ]), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var steps = await ReadStepsAsync(itemId);
        steps.Select(s => s.Id).Should().BeEquivalentTo([keptId, alsoKeptId],
            "the ids the client is holding are the ids the three step routes take in the path");
        steps.Single(s => s.Id == alsoKeptId).Name.Should().Be("second, renamed",
            "keeping the identity must not mean ignoring the edit");

        // The half that actually bites the user: the route the frontend calls next.
        var toggle = await client.PatchAsync(
            $"/api/todo-list-item/{itemId}/steps/{keptId}/toggle", null, CancellationToken);
        toggle.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "a step id that survived the PUT must still address a step");
    }

    /// <summary>
    /// <c>CQ-19</c>, second half. The old code read <c>StepRequest.Id</c> only to carry <c>IsDone</c>
    /// across, then discarded it — so the carry-over worked exactly once. On the next PUT the client was
    /// echoing ids that no longer existed and every ticked step silently reverted.
    /// </summary>
    [Fact(DisplayName = "CQ-19: two consecutive PUTs do not lose a ticked step")]
    public async Task UpdateItem_Twice_KeepsStepCompletion()
    {
        var stepId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, steps: [new TodoListStep { Id = stepId, Name = "ticked", Order = 0, IsDone = true }]);

        var client = CreateUserRoleClient();
        for (var i = 1; i <= 2; i++)
        {
            var response = await client.PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
                note: $"edit {i}",
                steps: [new { id = stepId, name = "ticked", order = 0 }]),
                JsonOpts, CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var steps = await ReadStepsAsync(itemId);
        steps.Single().Id.Should().Be(stepId);
        steps.Single().IsDone.Should().BeTrue(
            "completion rides on the step's identity, so it has to survive an arbitrary number of edits, not one");
    }

    /// <summary>Reconciliation is three-way: kept, added, removed — not "keep everything that has an id".</summary>
    [Fact(DisplayName = "CQ-19: a PUT adds the steps without an id and drops the ones it omits")]
    public async Task UpdateItem_AddsNewStepsAndDropsOmittedOnes()
    {
        var keptId = Guid.NewGuid();
        var droppedId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, steps:
            [
                new TodoListStep { Id = keptId, Name = "kept", Order = 0 },
                new TodoListStep { Id = droppedId, Name = "dropped", Order = 1 }
            ]);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            steps:
            [
                new { id = keptId, name = "kept", order = 0 },
                new { name = "brand new", order = 1 }
            ]), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var steps = await ReadStepsAsync(itemId);
        steps.Should().HaveCount(2);
        steps.Should().ContainSingle(s => s.Id == keptId);
        steps.Should().NotContain(s => s.Id == droppedId, "a step the request omits is gone");
        steps.Should().ContainSingle(s => s.Name == "brand new" && s.Id != Guid.Empty,
            "a step without an id is new and gets one minted");
    }

    // ---- SEC-5: DisplayOrder and DoneCount are server-owned -----------------------------------------

    /// <summary>
    /// <c>SEC-5</c>. The PUT could place an item at any <c>DisplayOrder</c>, including one already taken
    /// by a sibling — the exact collision <c>BaseChangeDisplayOrderTodoListEndpoint</c> exists to avoid.
    /// Within one user's own data, so this is integrity rather than authorization, but it made the
    /// ordering invariant client-enforced.
    /// </summary>
    [Fact(DisplayName = "SEC-5: a PUT cannot reposition an item")]
    public async Task UpdateItem_CannotWriteDisplayOrder()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, displayOrder: 4000);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            note: "edited", displayOrder: 7L), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId);
        item.DisplayOrder.Should().Be(4000, "display order belongs to the reorder endpoints, not to the edit");
        item.Note.Should().Be("edited", "the fields the PUT does own must still be written");
    }

    /// <summary>
    /// <c>SEC-5</c> and <c>CQ-9</c> together. Accepting <c>DoneCount</c> let a PUT claim any count the
    /// validator's <c>DoneCount &lt;= TotalCount</c> rule permitted, independently of how many steps were
    /// actually ticked — and it was one of the two ways to push the count past the total, which
    /// <c>IsDoneLogic</c>'s <c>==</c> arm then could not recover from.
    /// </summary>
    [Fact(DisplayName = "SEC-5: a PUT cannot set DoneCount out from under the steps")]
    public async Task UpdateItem_CannotWriteDoneCount()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, totalCount: 5, doneCount: 1);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            totalCount: 5, doneCount: 4), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId);
        item.DoneCount.Should().Be(1, "the count is derived from the toggles, not taken from the body");
    }

    /// <summary>
    /// The counted-item companion to <c>SEC-5</c>: with <c>DoneCount</c> no longer client-supplied,
    /// lowering <c>TotalCount</c> under the count already recorded has to clamp, or the save hits
    /// <c>CK_TodoListItem_DoneCount_LessOrEqual_TotalCount</c> on an ordinary edit.
    /// </summary>
    [Fact(DisplayName = "SEC-5: lowering TotalCount clamps the server-owned DoneCount instead of failing")]
    public async Task UpdateItem_LoweringTotalCount_ClampsDoneCount()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, totalCount: 5, doneCount: 4);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            totalCount: 2), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId);
        item.TotalCount.Should().Be(2);
        item.DoneCount.Should().Be(2);
    }

    /// <summary>An item that becomes counted starts at zero rather than at null.</summary>
    [Fact(DisplayName = "SEC-5: an item that gains a TotalCount starts its count at zero")]
    public async Task UpdateItem_GainingTotalCount_StartsAtZero()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            totalCount: 4), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId);
        item.DoneCount.Should().Be(0);
    }

    // ---- CQ-16: the three representations of "done" ------------------------------------------------

    /// <summary>
    /// <c>CQ-16</c>. <c>IsDone</c>, <c>DoneCount</c> and each step's <c>IsDone</c> were written in three
    /// independent places across two files, none of which consulted the others, so a PUT could produce
    /// <c>IsDone = true</c> with every step unticked. <c>BaseTodoListItem.SetDone</c> exists to snap all
    /// three together and nothing on this path called it.
    /// </summary>
    [Fact(DisplayName = "CQ-16: completing through the PUT snaps DoneCount and every step with IsDone")]
    public async Task UpdateItem_MarkingDone_SnapsCountAndSteps()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, totalCount: 3, doneCount: 1, steps:
            [
                new TodoListStep { Name = "a", Order = 0 },
                new TodoListStep { Name = "b", Order = 1 }
            ]);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            isDone: true, totalCount: 3), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId, includeSteps: true);
        item.IsDone.Should().BeTrue();
        item.DoneCount.Should().Be(3, "a done counted item is done all the way, not one tick short");
        item.Steps.Should().OnlyContain(s => s.IsDone, "the third representation has to move with the other two");
    }

    /// <summary>
    /// The other side of <c>CQ-16</c>'s fix, and the reason it is a flip test rather than an
    /// unconditional <c>SetDone</c>. <c>SetDone</c> is a snap, not a merge: called on an edit that never
    /// touched completion it would clear the count and untick every step, so changing a note would quietly
    /// wipe a half-finished checklist.
    /// </summary>
    [Fact(DisplayName = "CQ-16: an edit that does not touch IsDone leaves a half-finished checklist alone")]
    public async Task UpdateItem_WithoutChangingIsDone_LeavesProgressIntact()
    {
        var tickedId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, totalCount: 3, doneCount: 2, steps:
            [
                new TodoListStep { Id = tickedId, Name = "already ticked", Order = 0, IsDone = true },
                new TodoListStep { Name = "still open", Order = 1 }
            ]);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            isDone: false, totalCount: 3, note: "just fixing a typo",
            steps:
            [
                new { id = tickedId, name = "already ticked", order = 0 },
                new { name = "still open", order = 1 }
            ]), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId, includeSteps: true);
        item.DoneCount.Should().Be(2, "the item was not done before and is not done now — nothing about completion changed");
        item.Steps.Single(s => s.Id == tickedId).IsDone.Should().BeTrue();
    }

    /// <summary>Un-completing through the PUT matches what the toggle does with no forced value: a full reset.</summary>
    [Fact(DisplayName = "CQ-16: un-completing through the PUT resets the count and unticks every step")]
    public async Task UpdateItem_MarkingNotDone_ResetsCountAndSteps()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, totalCount: 2, doneCount: 2, isDone: true, steps:
            [
                new TodoListStep { Name = "a", Order = 0, IsDone = true },
                new TodoListStep { Name = "b", Order = 1, IsDone = true }
            ]);

        var response = await CreateUserRoleClient().PutAsJsonAsync($"/api/todo-list-item/{itemId}", Body(
            isDone: false, totalCount: 2), JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId, includeSteps: true);
        item.IsDone.Should().BeFalse();
        item.DoneCount.Should().Be(0, "the no-force untoggle is a reset, not a step back — SetDone and IsDoneLogic agree here");
        item.Steps.Should().OnlyContain(s => !s.IsDone);
    }

    // ---- helpers -----------------------------------------------------------------------------------

    /// <summary>
    /// The PUT body. <c>activityId</c> and <c>taskPriorityId</c> are required on every request and say
    /// nothing about any of these cases, so they live here rather than in each one.
    /// </summary>
    /// <remarks>
    /// <paramref name="displayOrder"/> and <paramref name="doneCount"/> have no matching property on
    /// <c>BaseUpdateTodoListRequest</c> — they are here to be ignored, which is the whole assertion in
    /// the two <c>SEC-5</c> cases.
    /// </remarks>
    private Dictionary<string, object?> Body(
        bool isDone = false, int? totalCount = null, string? note = null,
        object[]? steps = null, long? displayOrder = null, int? doneCount = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["activityId"] = _activityId,
            ["taskPriorityId"] = _priorityId,
            ["isDone"] = isDone
        };

        if (totalCount is not null) body["totalCount"] = totalCount;
        if (note is not null) body["note"] = note;
        if (steps is not null) body["steps"] = steps;
        if (displayOrder is not null) body["displayOrder"] = displayOrder;
        if (doneCount is not null) body["doneCount"] = doneCount;

        return body;
    }

    private Task<long> SeedItemAsync(
        DbContext db, int? totalCount = null, int? doneCount = null, bool isDone = false,
        List<TodoListStep>? steps = null, long displayOrder = 1000) =>
        TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, _activityId, _priorityId, _listId, UserId,
            totalCount: totalCount, doneCount: doneCount, isDone: isDone,
            steps: steps, displayOrder: displayOrder, ct: CancellationToken);

    private async Task<TodoListItem> ReadItemAsync(long itemId, bool includeSteps = false)
    {
        await using var db = CreateDbContext();
        var query = db.Set<TodoListItem>().IgnoreQueryFilters().AsNoTracking();
        if (includeSteps)
            query = query.Include(i => i.Steps);
        return await query.SingleAsync(i => i.Id == itemId, CancellationToken);
    }

    private async Task<List<TodoListStep>> ReadStepsAsync(long itemId) =>
        (await ReadItemAsync(itemId, includeSteps: true)).Steps.OrderBy(s => s.Order).ToList();
}
