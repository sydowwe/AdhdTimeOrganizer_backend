using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section C — checklist-step CRUD shared by both to-do flavours
/// (<c>BaseCreate|Update|DeleteStepEndpoint</c>), exercised through <c>TodoListItem</c>'s routes
/// (<c>/todo-list-item/{itemId}/steps[/...]</c>) and, once, through <c>RoutineTodoList</c>'s to confirm the
/// shared base behaves the same for both parents.
/// <para>
/// The spec assumes adding/removing steps interacts with <c>TotalCount</c> (its 2–99 range, its floor). It
/// doesn't: <c>BaseCreateStepEndpoint</c> and <c>BaseDeleteStepEndpoint</c> never read or write
/// <c>TotalCount</c> at all — that column and the owned <c>Steps</c> JSON collection are two entirely
/// independent tracks of "how much is there to do", both feeding <c>DoneCount</c>/<c>IsDone</c> through
/// different endpoints. This file pins the actual (decoupled) behaviour rather than the assumed one — see
/// each test's DisplayName for what it found.
/// </para>
/// </summary>
[Collection("Postgres")]
public class TodoListStepsCrudTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private async Task<long> SeedItemAsync(DbContext db, string label, int? totalCount = null, List<TodoListStep>? steps = null, long userId = TodoListTestSeedHelper.TestUserId)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"{label}-activity", userId);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, Random.Shared.Next(1000, 999_000), userId, text: $"{label}-priority");
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, userId, $"{label}-list");
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId, userId, totalCount: totalCount, steps: steps);
    }

    [Fact(DisplayName = "Adding a step does not touch TotalCount -- it is tracked entirely separately")]
    public async Task CreateStep_DoesNotChangeTotalCount()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "step-create", totalCount: 5);

        var response = await CreateClient().PostAsJsonAsync($"api/todo-list-item/{itemId}/steps", new
        {
            ItemId = itemId,
            Name = "New step",
            Order = 0
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken);
        item.Steps.Should().ContainSingle(s => s.Name == "New step");
        item.TotalCount.Should().Be(5, "BaseCreateStepEndpoint never reads or writes TotalCount");
    }

    [Fact(DisplayName = "Creating steps well past 99 succeeds cleanly -- there is no cap tied to the Steps collection")]
    public async Task CreateStep_PastNinetyNine_StillSucceeds()
    {
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "step-overflow", totalCount: null,
                steps: Enumerable.Range(0, 100).Select(i => new TodoListStep { Name = $"s{i}", Order = i }).ToList());

        var response = await CreateClient().PostAsJsonAsync($"api/todo-list-item/{itemId}/steps", new
        {
            ItemId = itemId,
            Name = "Step 101",
            Order = 100
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "TotalCount's 2-99 DB check constraint applies only to that column, not to Steps.Count, so this is a genuine gap in enforcement if the client ever assumed a 99-step cap");

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken);
        item.Steps.Should().HaveCount(101);
    }

    [Fact(DisplayName = "Updating a step's Name/Order/Note persists and preserves its IsDone flag")]
    public async Task UpdateStep_PersistsFieldsAndPreservesIsDone()
    {
        var stepId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "step-update", steps: [new TodoListStep { Id = stepId, Name = "Original", Order = 0, IsDone = true }]);

        var response = await CreateClient().PutAsJsonAsync($"api/todo-list-item/{itemId}/steps/{stepId}", new
        {
            ItemId = itemId,
            StepId = stepId,
            Name = "Renamed",
            Order = 1,
            Note = "a note"
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken);
        var step = item.Steps.Single(s => s.Id == stepId);
        step.Name.Should().Be("Renamed");
        step.Order.Should().Be(1);
        step.Note.Should().Be("a note");
    }

    [Fact(DisplayName = "Deleting the second-to-last step of a 2-step item succeeds -- no floor is enforced on Steps.Count")]
    public async Task DeleteStep_SecondToLastOfTwo_SucceedsWithNoFloorEnforced()
    {
        var stepAId = Guid.NewGuid();
        var stepBId = Guid.NewGuid();
        long itemId;
        await using (var db = CreateDbContext())
            itemId = await SeedItemAsync(db, "step-floor", totalCount: 2,
                steps: [new TodoListStep { Id = stepAId, Name = "A", Order = 0 }, new TodoListStep { Id = stepBId, Name = "B", Order = 1 }]);

        var response = await CreateClient().DeleteAsync($"api/todo-list-item/{itemId}/steps/{stepAId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the TotalCount>=2 check constraint binds the TotalCount column, which BaseDeleteStepEndpoint never touches -- " +
            "there is no equivalent floor on the Steps collection itself, contrary to what the spec assumed");

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken);
        item.Steps.Should().ContainSingle(s => s.Id == stepBId);
        item.TotalCount.Should().Be(2, "TotalCount itself is untouched by the delete");
    }

    [Fact(DisplayName = "A step id addressed through another user's item id 404s (item lookup is user-scoped)")]
    public async Task UpdateStep_ForeignItemId_Returns404()
    {
        long otherUserId = ReminderSeedHelper.OtherUserId;
        var theirStepId = Guid.NewGuid();
        long theirItemId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirItemId = await SeedItemAsync(db, "step-idor", steps: [new TodoListStep { Id = theirStepId, Name = "Theirs", Order = 0 }], userId: otherUserId);
        }

        var response = await CreateClient().PutAsJsonAsync($"api/todo-list-item/{theirItemId}/steps/{theirStepId}", new
        {
            ItemId = theirItemId,
            StepId = theirStepId,
            Name = "Hijacked",
            Order = 0
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "GetParentQuery filters by e.UserId == caller's id, so another user's item id resolves to nothing");

        await using var verifyDb = CreateDbContext();
        var theirs = await verifyDb.Set<TodoListItem>().IgnoreQueryFilters().Include(i => i.Steps).SingleAsync(i => i.Id == theirItemId, CancellationToken);
        theirs.Steps.Single().Name.Should().Be("Theirs", "the cross-user update must not have applied");
    }

    [Fact(DisplayName = "The same shared step base behaves identically for RoutineTodoList")]
    public async Task CreateStep_OnRoutineTodoList_AlsoWorks()
    {
        long itemId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "routine-step-create");
            var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "routine-step-create-period");
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId);
        }

        var response = await CreateClient().PostAsJsonAsync($"api/routine-todo-list/{itemId}/steps", new
        {
            ItemId = itemId,
            Name = "Routine step",
            Order = 0
        }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<RoutineTodoList>().Include(i => i.Steps).SingleAsync(i => i.Id == itemId, CancellationToken);
        item.Steps.Should().ContainSingle(s => s.Name == "Routine step");
    }
}
