using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.ActivityProfiles.domain.model.entity;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// The `ActivityId` every `IActivityIdRequest` carries, on the four endpoints that write it without a
/// validator rule of their own: to-do item create/update and routine item create/update. Plus the two
/// memory-anchor cases, which are the exception in both directions.
/// </summary>
/// <remarks>
/// <para>
/// Same hole as <see cref="ActivityHistoryReferencedIdOwnershipTests"/> and the same reason it is
/// invisible: the row is stamped with the caller's own `UserId`, so a request naming a stranger's
/// sequential id creates a valid task *of the caller's* pointing at somebody else's activity. The FK
/// enforces existence, not ownership. The symptom is not an exception but a task whose activity every
/// read loses to the global query filter.
/// </para>
/// <para>
/// The guard is `ActivityIdOwnershipExtensions.EnsureOwnsActivityAsync`, called from `BeforeMapping`.
/// The update cases matter as much as the creates: `UpdateEntity` writes `ActivityId`, so an edit can
/// repoint a row the caller legitimately owns.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class ActivityIdOwnershipTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private const long UserId = FakeLoggedUserService.TestUserId;

    // ---- to-do items -----------------------------------------------------------------------------

    [Fact(DisplayName = "Creating a to-do item on another user's activity is rejected")]
    public async Task CreateTodoListItem_WithForeignActivityId_IsRejected()
    {
        var foreignActivityId = await SeedForeignActivityAsync();
        long priorityId, listId;
        await using (var db = CreateDbContext())
        {
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/todo-list-item",
            new { activityId = foreignActivityId, taskPriorityId = priorityId, todoListId = listId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an activity the caller does not own must read as one that does not exist");
        (await CountAsync<TodoListItem>(foreignActivityId)).Should().Be(0);
    }

    /// <summary>
    /// The control for the rejection above — without it the endpoint could be hard-wired to 404.
    /// </summary>
    [Fact(DisplayName = "Creating a to-do item on the caller's own activity still succeeds")]
    public async Task CreateTodoListItem_WithOwnActivityId_Succeeds()
    {
        long activityId, priorityId, listId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own todo activity", ct: CancellationToken);
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
            listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/todo-list-item",
            new { activityId, taskPriorityId = priorityId, todoListId = listId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "Repointing a to-do item at another user's activity is rejected")]
    public async Task UpdateTodoListItem_WithForeignActivityId_IsRejected()
    {
        var foreignActivityId = await SeedForeignActivityAsync();
        long ownActivityId, priorityId, itemId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own todo before edit", ct: CancellationToken);
            priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
            var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, ownActivityId, priorityId, listId, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/todo-list-item/{itemId}",
            new { activityId = foreignActivityId, taskPriorityId = priorityId, displayOrder = 1000L, isDone = false },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var check = CreateDbContext();
        (await check.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken))
            .ActivityId.Should().Be(ownActivityId, "the refused edit must not have been applied");
    }

    // ---- routine items ---------------------------------------------------------------------------

    [Fact(DisplayName = "Creating a routine item on another user's activity is rejected")]
    public async Task CreateRoutineTodoList_WithForeignActivityId_IsRejected()
    {
        var foreignActivityId = await SeedForeignActivityAsync();
        long periodId;
        await using (var db = CreateDbContext())
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/routine-todo-list",
            new { activityId = foreignActivityId, timePeriodId = periodId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountAsync<RoutineTodoList>(foreignActivityId)).Should().Be(0);
    }

    [Fact(DisplayName = "Creating a routine item on the caller's own activity still succeeds")]
    public async Task CreateRoutineTodoList_WithOwnActivityId_Succeeds()
    {
        long activityId, periodId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own routine activity", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/routine-todo-list",
            new { activityId, timePeriodId = periodId },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "Repointing a routine item at another user's activity is rejected")]
    public async Task UpdateRoutineTodoList_WithForeignActivityId_IsRejected()
    {
        var foreignActivityId = await SeedForeignActivityAsync();
        long ownActivityId, periodId, itemId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own routine before edit", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, ownActivityId, periodId, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new { activityId = foreignActivityId, timePeriodId = periodId, displayOrder = 1000L, isDone = false },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var check = CreateDbContext();
        (await check.Set<RoutineTodoList>().SingleAsync(r => r.Id == itemId, CancellationToken))
            .ActivityId.Should().Be(ownActivityId, "the refused edit must not have been applied");
    }

    // ---- memory anchors --------------------------------------------------------------------------

    [Fact(DisplayName = "Repointing a memory anchor at another user's activity is rejected")]
    public async Task UpdateMemoryAnchor_WithForeignActivityId_IsRejected()
    {
        var foreignActivityId = await SeedForeignActivityAsync();
        long ownActivityId, anchorId;
        await using (var db = CreateDbContext())
        {
            ownActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Own anchor activity", ct: CancellationToken);
            var anchor = new MemoryAnchor
            {
                UserId = UserId,
                ActivityId = ownActivityId,
                AnchorMonth = 6,
                AnchorYear = 2025,
                HighlightNote = "Original note",
                Rating = 7
            };
            db.Set<MemoryAnchor>().Add(anchor);
            await db.SaveChangesAsync(CancellationToken);
            anchorId = anchor.Id;
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/memory-anchor/{anchorId}",
            new
            {
                activityId = foreignActivityId,
                anchorMonth = 7,
                anchorYear = 2025,
                highlightNote = "Updated note",
                rating = 9
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "UpdateMemoryAnchorValidator has no ownership rule -- unlike the create one, this path has "
            + "only the endpoint guard, which refuses a foreign activityId outright rather than letting "
            + "MemoryAnchorRequest.UpdateEntity silently ignore it (see MemoryAnchorUpdateRulesTests)");

        await using var check = CreateDbContext();
        (await check.Set<MemoryAnchor>().SingleAsync(a => a.Id == anchorId, CancellationToken))
            .ActivityId.Should().Be(ownActivityId);
    }

    /// <summary>
    /// Why <c>CreateMemoryAnchorEndpoint</c> is the one endpoint on this DTO family with no
    /// <c>BeforeMapping</c> guard: its validator already resolves the activity as the caller's on the
    /// way to the backlog/bucket-list rule, and rejects a foreign id with a 400 before the handler
    /// runs. This pins that, so deleting the validator's <c>a.UserId == userId</c> predicate does not
    /// silently re-open the hole the missing override assumes is closed.
    /// </summary>
    [Fact(DisplayName = "Creating a memory anchor on another user's activity is refused by the validator")]
    public async Task CreateMemoryAnchor_WithForeignActivityId_IsRefusedByTheValidator()
    {
        var foreignActivityId = await SeedForeignActivityAsync();

        var response = await CreateUserRoleClient().PostAsJsonAsync(
            "/api/memory-anchor",
            new
            {
                activityId = foreignActivityId,
                anchorMonth = 7,
                anchorYear = 2025,
                highlightNote = "Anchor on a stranger's activity",
                rating = 9
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync<MemoryAnchor>(foreignActivityId)).Should().Be(0);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private async Task<int> CountAsync<TEntity>(long activityId) where TEntity : class
    {
        await using var db = CreateDbContext();
        return await db.Set<TEntity>()
            .IgnoreQueryFilters()
            .CountAsync(e => EF.Property<long>(e, "ActivityId") == activityId, CancellationToken);
    }

    private async Task<long> SeedForeignActivityAsync()
    {
        var otherUserId = await ActivityMergeTestSupport.SecondUserAsync(
            Fixture, $"activity-id-ownership-{Guid.NewGuid():N}@test.com");

        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, "Foreign activity", otherUserId, CancellationToken);
    }
}
