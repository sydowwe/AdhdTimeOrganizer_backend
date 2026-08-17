using System.Net;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section G — delete behavior for the three cross-entity FKs the spec calls out:
/// <c>RoutineTimePeriod</c> → <c>RoutineTodoList</c> (Restrict), <c>TaskPriority</c> → <c>TodoListItem</c>
/// (Restrict) and <c>TodoListCategory</c> → <c>TodoList</c> (SetNull).
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
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "restrict-period");
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "restrict-period-activity");
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId);
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
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 700, text: "restrict-priority");
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "restrict-priority-activity");
            var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "restrict-priority-list");
            itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId);
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
            categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, name: "setnull-category");
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "setnull-list", categoryId: categoryId);
        }

        var response = await CreateClient().DeleteAsync($"api/todo-list-category/{categoryId}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var verifyDb = CreateDbContext();
        (await verifyDb.Set<TodoListCategory>().SingleOrDefaultAsync(c => c.Id == categoryId, CancellationToken)).Should().BeNull();
        var survivingList = await verifyDb.Set<TodoList>().SingleAsync(l => l.Id == listId, CancellationToken);
        survivingList.CategoryId.Should().BeNull();
    }
}
