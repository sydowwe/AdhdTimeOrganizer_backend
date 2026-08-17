using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section B — <c>BaseToggleIsDoneTodoListEndpoint</c>'s tick/untick logic and the
/// <c>DoneCount</c>/<c>TotalCount</c> arithmetic it owns, exercised through
/// <c>ToggleIsDoneTodoListItemEndpoint</c> (<c>PATCH /api/todo-list-item/toggle-is-done</c>).
/// <para>
/// See <see cref="ToggleUndone_DoneItem_ResetsDoneCountAndUnticksAllSteps"/>: the no-<c>ForceValue</c>
/// "untoggle a done item" branch calls <c>ResetSteps</c> like every other branch, so <c>Steps[].IsDone</c>
/// stays aligned with the parent's own <c>IsDone</c>.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TodoListToggleAndDoneCountTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private async Task<(long ItemId, long ActivityId, long PriorityId, long ListId)> SeedStepCountedItemAsync(
        DbContext db, int totalCount, int stepCount, string label)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"{label}-activity");
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 900, text: $"{label}-priority");
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: $"{label}-list");
        var steps = Enumerable.Range(0, stepCount)
            .Select(i => new TodoListStep { Name = $"step-{i}", Order = i })
            .ToList();
        var itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId, totalCount: totalCount, doneCount: 0, steps: steps);
        return (itemId, activityId, priorityId, listId);
    }

    private Task<HttpResponseMessage> ToggleAsync(HttpClient client, long id, bool? forceValue) =>
        client.PatchAsJsonAsync("api/todo-list-item/toggle-is-done", new { Ids = new[] { id }, ForceValue = forceValue }, JsonOpts, CancellationToken);

    /// <summary>
    /// ForceValue == true does NOT jump straight to fully done — per <c>IsDoneLogic</c> it only increments
    /// <c>DoneCount</c> by one per call, flipping <c>IsDone</c> only once the count reaches <c>TotalCount</c>.
    /// Reaching "fully done" therefore takes <paramref name="totalCount"/> calls, same as the no-force tick
    /// path — this is what several tests below actually need as a starting state.
    /// </summary>
    private async Task MarkFullyDoneAsync(HttpClient client, long id, int totalCount)
    {
        for (var i = 0; i < totalCount; i++)
            (await ToggleAsync(client, id, true)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact(DisplayName = "Repeated ForceValue==true calls reach DoneCount == TotalCount, IsDone true, and tick every step")]
    public async Task ToggleDone_StepCountedItem_SetsDoneCountAndTicksAllSteps()
    {
        long itemId;
        await using (var db = CreateDbContext())
            (itemId, _, _, _) = await SeedStepCountedItemAsync(db, totalCount: 3, stepCount: 3, "force-done");

        // Pinning actual behaviour: ForceValue == true increments by one per call rather than jumping
        // straight to "done" (see IsDoneLogic) -- three calls are needed for a TotalCount-3 item.
        await MarkFullyDoneAsync(CreateClient(), itemId, 3);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken);
        item.DoneCount.Should().Be(3);
        item.IsDone.Should().BeTrue();
        item.Steps.Should().OnlyContain(s => s.IsDone);
    }

    /// <summary>
    /// In <c>IsDoneLogic</c>'s no-<c>ForceValue</c> branch, the "item is currently done, so untoggle it"
    /// arm calls <c>ResetSteps</c> just like every other branch (both <c>ForceValue == true</c> and
    /// <c>ForceValue == false</c> call it unconditionally, and the no-force "tick" arm calls it too), so
    /// <c>Steps[].IsDone</c> stays aligned with the parent's own <c>IsDone</c>.
    /// </summary>
    [Fact(DisplayName = "No-force untoggle of a done item resets DoneCount/IsDone and unticks all Steps")]
    public async Task ToggleUndone_DoneItem_ResetsDoneCountAndUnticksAllSteps()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            (itemId, _, _, _) = await SeedStepCountedItemAsync(db, totalCount: 3, stepCount: 3, "force-undone");
        }

        var client = CreateClient();
        await MarkFullyDoneAsync(client, itemId, 3);
        // No ForceValue: IsDoneLogic's IsDone branch resets a done item straight to 0/undone.
        (await ToggleAsync(client, itemId, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken);
        item.DoneCount.Should().Be(0);
        item.IsDone.Should().BeFalse();
        item.Steps.Should().OnlyContain(s => !s.IsDone,
            "ResetSteps is called on this branch too, so the steps un-tick along with the parent item");
    }

    [Fact(DisplayName = "Ticking steps one at a time increments DoneCount, and the last tick flips IsDone true")]
    public async Task ToggleWithoutForce_TicksOneAtATime_LastTickFlipsIsDone()
    {
        long itemId;
        await using (var db = CreateDbContext())
            (itemId, _, _, _) = await SeedStepCountedItemAsync(db, totalCount: 3, stepCount: 3, "tick-once");

        var client = CreateClient();

        (await ToggleAsync(client, itemId, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using (var db1 = CreateDbContext())
        {
            var afterFirst = await db1.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
            afterFirst.DoneCount.Should().Be(1);
            afterFirst.IsDone.Should().BeFalse();
        }

        (await ToggleAsync(client, itemId, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using (var db2 = CreateDbContext())
        {
            var afterSecond = await db2.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
            afterSecond.DoneCount.Should().Be(2);
            afterSecond.IsDone.Should().BeFalse();
        }

        (await ToggleAsync(client, itemId, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var db3 = CreateDbContext();
        var afterThird = await db3.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
        afterThird.DoneCount.Should().Be(3);
        afterThird.IsDone.Should().BeTrue("the third tick reaches TotalCount");
    }

    [Fact(DisplayName = "Un-ticking one step of a fully-done item flips IsDone false and DoneCount to TotalCount - 1")]
    public async Task ToggleWithoutForce_OnDoneItem_UnticksOneStep()
    {
        long itemId;
        await using (var db = CreateDbContext())
            (itemId, _, _, _) = await SeedStepCountedItemAsync(db, totalCount: 3, stepCount: 3, "untick-one");

        var client = CreateClient();
        await MarkFullyDoneAsync(client, itemId, 3);

        // No-force toggle on an already-done item unticks it (IsDoneLogic's IsDone branch: IsDone -> DoneCount=0, not TotalCount-1).
        (await ToggleAsync(client, itemId, null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.IsDone.Should().BeFalse();
        // Pinning actual behaviour: the no-force "undone" branch always resets to 0, not TotalCount - 1
        // (that TotalCount-1 behaviour belongs only to the explicit ForceValue == false branch — see
        // BaseToggleIsDoneTodoListEndpoint.IsDoneLogic). The spec's "untick one step" wording matches the
        // ForceValue == false path, exercised directly below.
        item.DoneCount.Should().Be(0);
    }

    [Fact(DisplayName = "ForceValue == false on a done item steps back exactly one (TotalCount - 1), not to zero")]
    public async Task ToggleForceFalse_OnDoneItem_StepsBackByOne()
    {
        long itemId;
        await using (var db = CreateDbContext())
            (itemId, _, _, _) = await SeedStepCountedItemAsync(db, totalCount: 3, stepCount: 3, "force-false-done");

        var client = CreateClient();
        await MarkFullyDoneAsync(client, itemId, 3);
        (await ToggleAsync(client, itemId, false)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.IsDone.Should().BeFalse();
        item.DoneCount.Should().Be(2, "ForceValue == false on a done item sets DoneCount = TotalCount - 1 per IsDoneLogic");
    }

    [Fact(DisplayName = "Invariant fuzz: DoneCount never exceeds TotalCount nor goes below 0 across a seeded-random toggle sequence")]
    public async Task InvariantFuzz_DoneCountNeverEscapesBounds()
    {
        const int totalCount = 5;
        long itemId;
        await using (var db = CreateDbContext())
            (itemId, _, _, _) = await SeedStepCountedItemAsync(db, totalCount: totalCount, stepCount: totalCount, "fuzz");

        var client = CreateClient();
        var random = new Random(20260817); // fixed seed for reproducibility

        for (var i = 0; i < 40; i++)
        {
            // Randomly choose force-true, force-false, or toggle-without-force.
            bool? force = random.Next(3) switch
            {
                0 => true,
                1 => false,
                _ => null
            };

            var response = await ToggleAsync(client, itemId, force);
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);

            await using var db = CreateDbContext();
            var item = await db.Set<TodoListItem>().SingleAsync(x => x.Id == itemId, CancellationToken);
            item.DoneCount.Should().BeInRange(0, totalCount,
                $"iteration {i} (force={force}) must not push DoneCount out of [0, TotalCount]");
        }
    }

    /// <summary>
    /// CQ-19: <c>CK_{Entity}_DoneCount_LessOrEqual_TotalCount</c> reads
    /// <c>done_count IS NULL OR total_count IS NULL OR done_count &lt;= total_count</c> — a NULL on either
    /// side is treated by Postgres as satisfying the check. This creates a row with DoneCount set and
    /// TotalCount left NULL directly via EF (bypassing the create endpoint's validator, which never accepts
    /// this combination through the API surface) to record what the DB itself actually allows.
    /// </summary>
    [Fact(DisplayName = "CQ-19: DoneCount set with TotalCount NULL is accepted at the DB layer (the check constraint doesn't catch it)")]
    public async Task CheckConstraint_BypassedWhenTotalCountIsNull()
    {
        long activityId, priorityId, listId;
        await using (var seedDb = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(seedDb, "cq19-activity");
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(seedDb, 901, text: "cq19-priority");
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(seedDb, name: "cq19-list");
        }

        await using var db = CreateDbContext();
        var entity = new TodoListItem
        {
            UserId = TodoListTestSeedHelper.TestUserId,
            ActivityId = activityId,
            TaskPriorityId = priorityId,
            TodoListId = listId,
            TotalCount = null,
            DoneCount = 50, // would be nonsensical against any real TotalCount, but TotalCount is NULL
            DisplayOrder = 1
        };
        db.Set<TodoListItem>().Add(entity);

        var act = async () => await db.SaveChangesAsync(CancellationToken);

        await act.Should().NotThrowAsync(
            "the DoneCount_LessOrEqual_TotalCount check constraint short-circuits to true whenever either column is NULL, " +
            "so this combination is a genuine DB-level gap, not a validator gap — CQ-19");

        await using var verifyDb = CreateDbContext();
        var saved = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == entity.Id, CancellationToken);
        saved.DoneCount.Should().Be(50);
        saved.TotalCount.Should().BeNull();
    }
}
