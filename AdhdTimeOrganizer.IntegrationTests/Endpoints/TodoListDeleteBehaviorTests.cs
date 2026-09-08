using System.Net;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section G — delete behavior for the three cross-entity FKs the spec calls out:
/// <c>RoutineTimePeriod</c> → <c>RoutineTodoList</c> (Restrict), <c>TaskPriority</c> → <c>TodoListItem</c>
/// (Restrict) and <c>TodoListCategory</c> → <c>TodoList</c> (SetNull) — plus, from TEST-7, the fourth FK
/// in this area that the original three left out: <c>TodoList</c> → <c>TodoListItem</c> (Cascade).
/// </summary>
[Collection("Postgres")]
public class TodoListDeleteBehaviorTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    [Fact(DisplayName = "Deleting a RoutineTimePeriod that still has routine items is Restricted -- clean 409")]
    public async Task DeleteRoutineTimePeriod_WithItems_Returns409()
    {
        long periodId, itemId;
        await using (var db = CreateDbContext())
        {
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "restrict-period", ct: TestContext.Current.CancellationToken);
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "restrict-period-activity", ct: TestContext.Current.CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, ct: TestContext.Current.CancellationToken);
        }

        var response = await CreateClient().DeleteAsync($"api/routine-time-period/{periodId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<RoutineTimePeriod>().SingleOrDefaultAsync(p => p.Id == periodId, CancellationToken)).Should().NotBeNull();
        (await verifyDb.Set<RoutineTodoList>().SingleOrDefaultAsync(i => i.Id == itemId, CancellationToken)).Should().NotBeNull();
    }

    [Fact(DisplayName = "Deleting a TaskPriority still referenced by items is Restricted -- clean 409")]
    public async Task DeleteTaskPriority_WithItems_Returns409()
    {
        long priorityId, itemId;
        await using (var db = CreateDbContext())
        {
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 700, text: "restrict-priority", ct: TestContext.Current.CancellationToken);
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "restrict-priority-activity", ct: TestContext.Current.CancellationToken);
            var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "restrict-priority-list", ct: TestContext.Current.CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId, ct: TestContext.Current.CancellationToken);
        }

        var response = await CreateClient().DeleteAsync($"api/task-priority/{priorityId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TaskPriority>().SingleOrDefaultAsync(p => p.Id == priorityId, CancellationToken)).Should().NotBeNull();
        (await verifyDb.Set<TodoListItem>().SingleOrDefaultAsync(i => i.Id == itemId, CancellationToken)).Should().NotBeNull();
    }

    [Fact(DisplayName = "Deleting a TodoListCategory SetNulls its lists -- the lists survive with a null category")]
    public async Task DeleteTodoListCategory_SetsListsCategoryToNull()
    {
        long categoryId, listId;
        await using (var db = CreateDbContext())
        {
            categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "setnull-category", ct: TestContext.Current.CancellationToken);
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "setnull-list", categoryId: categoryId, ct: TestContext.Current.CancellationToken);
        }

        var response = await CreateClient().DeleteAsync($"api/todo-list-category/{categoryId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListCategory>().SingleOrDefaultAsync(c => c.Id == categoryId, CancellationToken)).Should().BeNull();
        var survivingList = await verifyDb.Set<TodoList>().SingleAsync(l => l.Id == listId, CancellationToken);
        survivingList.CategoryId.Should().BeNull();
    }

    /// <summary>
    /// TEST-12 — the fourth FK. <c>TodoListConfiguration</c> cascades <c>TodoList</c> → <c>TodoListItem</c>,
    /// and the second half is what makes the cascade safe: <c>ActivityHistory.TodoListItemId</c> is
    /// <c>SetNull</c>, declared host-side in <c>AppDbContext.ConfigureCrossSliceRelationships</c> where
    /// deleting it is not a build error. Flip it to Cascade and tidying up a to-do list quietly deletes the
    /// time the user recorded against it — the same argument <c>TodoListItemDailyRecapTests</c> makes for a
    /// single item's delete, which this extends to the list above it.
    /// </summary>
    [Fact(DisplayName = "Deleting a TodoList cascades its items -- but the ActivityHistory recorded against them survives, unlinked")]
    public async Task DeleteTodoList_CascadesItems_AndKeepsTheirActivityHistory()
    {
        long listId, itemAId, itemBId, activityAId, activityBId;
        await using (var db = CreateDbContext())
        {
            var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 701, text: "cascade-priority", ct: CancellationToken);
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "cascade-list", ct: CancellationToken);

            // One activity per item: todo_list_item is unique on (UserId, ActivityId, TodoListId).
            activityAId = await PlanningTestSeedHelper.SeedActivityAsync(db, "cascade-activity-a", ct: CancellationToken);
            activityBId = await PlanningTestSeedHelper.SeedActivityAsync(db, "cascade-activity-b", ct: CancellationToken);
            itemAId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityAId, priorityId, listId, ct: CancellationToken);
            itemBId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityBId, priorityId, listId, ct: CancellationToken);

            // Recorded the way TodoListItemDailyRecapTests records it — the save-to-history prompt's shape.
            var startUtc = DateTime.UtcNow.Date.AddHours(9);
            db.Set<ActivityHistory>().AddRange(
                NewRecording(activityAId, itemAId, startUtc),
                NewRecording(activityBId, itemBId, startUtc));
            await db.SaveChangesAsync(CancellationToken);
        }

        var response = await CreateClient().DeleteAsync($"api/todo-list/{listId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();

        // IgnoreQueryFilters so a soft-delete or a new filter cannot fake the cascade.
        (await verifyDb.Set<TodoListItem>().IgnoreQueryFilters()
                .CountAsync(i => i.Id == itemAId || i.Id == itemBId, CancellationToken))
            .Should().Be(0, "the FK is Cascade, so both items go with the list");

        var recordings = await verifyDb.Set<ActivityHistory>().IgnoreQueryFilters()
            .Where(h => h.ActivityId == activityAId || h.ActivityId == activityBId)
            .ToListAsync(CancellationToken);

        recordings.Should().HaveCount(2, "cascading a list must not become a way to delete recorded time");
        recordings.Should().OnlyContain(h => h.TodoListItemId == null);
    }

    private static ActivityHistory NewRecording(long activityId, long todoListItemId, DateTime startUtc) => new()
    {
        UserId = TodoListTestSeedHelper.TestUserId,
        ActivityId = activityId,
        StartTimestamp = startUtc,
        EndTimestamp = startUtc.AddSeconds(1200),
        Length = new IntTime(1200),
        TodoListItemId = todoListItemId
    };
}
