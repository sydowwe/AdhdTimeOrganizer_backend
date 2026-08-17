using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.Routines.application.dto.response.todoList;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// TEST-4 / Section I — <c>GetAllGroupedRoutineTodoListEndpoint</c> (<c>GET
/// /api/routine-todo-list/grouped-by-time-period</c>), the routines screen's main query, and
/// <c>MoveTodoListItemEndpoint</c> (<c>PATCH /api/todo-list-item/{id}/move</c>).
/// </summary>
[Collection("Postgres")]
public class RoutineGroupedReadAndMoveItemTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    // ---- GetAllGroupedRoutineTodoListEndpoint ---------------------------------------------------------

    [Fact(DisplayName = "Grouped read groups items by their own time period and scopes to the caller's user")]
    public async Task GroupedRead_GroupsByPeriod_AndScopesToCallingUser()
    {
        long periodAId, periodBId;
        await using (var db = CreateDbContext())
        {
            periodAId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "grouped-period-a", lengthInDays: 30, resetAnchorDay: 0);
            periodBId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, text: "grouped-period-b", lengthInDays: 45, resetAnchorDay: 0);

            var activityA1 = await PlanningTestSeedHelper.SeedActivityAsync(db, "grouped-a1");
            var activityA2 = await PlanningTestSeedHelper.SeedActivityAsync(db, "grouped-a2");
            var activityB1 = await PlanningTestSeedHelper.SeedActivityAsync(db, "grouped-b1");

            await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityA1, periodAId);
            await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityA2, periodAId);
            await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityB1, periodBId);

            // A different user's period+item must never appear in this user's grouped response.
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var otherPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                db, ReminderSeedHelper.OtherUserId, text: "grouped-period-theirs", lengthInDays: 30, resetAnchorDay: 0);
            var otherActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "grouped-theirs", ReminderSeedHelper.OtherUserId);
            await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, otherActivityId, otherPeriodId, ReminderSeedHelper.OtherUserId);
        }

        var response = await CreateClient().GetAsync("api/routine-todo-list/grouped-by-time-period", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var groups = await response.Content.ReadFromJsonAsync<List<RoutineTodoListGroupedResponse>>(JsonOpts, CancellationToken);
        groups.Should().NotBeNull();

        var periodIds = groups!.Select(g => g.RoutineTimePeriod.Id).ToList();
        periodIds.Should().BeEquivalentTo([periodAId, periodBId], "only the caller's own two periods must appear");

        var groupA = groups.Single(g => g.RoutineTimePeriod.Id == periodAId);
        groupA.Items.Should().HaveCount(2, "period A owns two items");

        var groupB = groups.Single(g => g.RoutineTimePeriod.Id == periodBId);
        groupB.Items.Should().HaveCount(1, "period B owns one item");
    }

    [Fact(DisplayName = "Grouped read is a real reset site: an elapsed period is reset and its streak evaluated, not just passively read")]
    public async Task GroupedRead_ElapsedPeriod_TriggersResetAndStreakEvaluation()
    {
        long periodId, itemId;
        await using (var db = CreateDbContext())
        {
            // ResetAnchorDay = 0 ("rolling"): next reset is exactly LastResetAt + LengthInDays, so seeding
            // LastResetAt far enough in the past guarantees the period has elapsed by "now".
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(
                db, text: "grouped-reset-period", lengthInDays: 7, resetAnchorDay: 0, streakThreshold: 50);

            var period = await db.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == periodId, CancellationToken);
            period.LastResetAt = DateTime.UtcNow.AddDays(-30);
            await db.SaveChangesAsync(CancellationToken);

            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "grouped-reset-activity");
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, isDone: true);
        }

        var response = await CreateClient().GetAsync("api/routine-todo-list/grouped-by-time-period", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var verifyDb = CreateDbContext();
        var storedPeriod = await verifyDb.Set<RoutineTimePeriod>().SingleAsync(p => p.Id == periodId, CancellationToken);
        storedPeriod.LastResetAt.Should().NotBe(DateTime.UtcNow.AddDays(-30), "the elapsed period must have been advanced by the read");
        storedPeriod.Streak.Should().Be(1, "the single item was fully done (100% >= 50% threshold), so the streak must have been evaluated and extended");

        var completions = await verifyDb.Set<RoutinePeriodCompletion>().Where(c => c.TimePeriodId == periodId).ToListAsync(CancellationToken);
        completions.Should().ContainSingle("a real reset must produce exactly one completion history row, proving the read did not just passively serve stale data");

        var item = await verifyDb.Set<RoutineTodoList>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.IsDone.Should().BeFalse("TryReset un-ticks every item once the period rolls over");
    }

    // ---- MoveTodoListItemEndpoint ----------------------------------------------------------------------

    /// <summary>
    /// <c>MoveTodoListItemEndpoint</c> overrides <c>HandleAsync</c> directly instead of using
    /// <c>Mapping</c> + the base class's flow, so it needs its own try/catch mirroring
    /// <c>BasePatchEndpoint</c>'s (<c>DbUtils.HandleException</c> + <c>EndpointHelper.ToStatusCode</c>) to
    /// give a unique-index collision on <c>(UserId, ActivityId, TodoListId)</c> the same clean 409 every
    /// other reorder/update path in this API gives — see <see cref="TaskPriorityReorderTests"/> /
    /// <see cref="TaskImportanceReorderTests"/>.
    /// </summary>
    [Fact(DisplayName = "Moving an item onto an activity already in the destination list 409s cleanly")]
    public async Task Move_ActivityAlreadyInDestinationList_Returns409()
    {
        long itemToMoveId, destinationListId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "move-collide-activity");
            var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 700, text: "move-collide-priority");

            var sourceListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "move-collide-source");
            destinationListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "move-collide-dest");

            itemToMoveId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, sourceListId);
            // Same ActivityId already sitting in the destination list -- (UserId, ActivityId, TodoListId) collides.
            await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, destinationListId);
        }

        var response = await CreateClient().PatchAsJsonAsync(
            $"api/todo-list-item/{itemToMoveId}/move", new { DestinationListId = destinationListId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "MoveTodoListItemEndpoint now wraps SaveChangesAsync the same way BasePatchEndpoint does, so the " +
            "unique-index violation maps to a clean 409 instead of an unhandled 500");

        await using var verifyDb = CreateDbContext();
        var moved = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemToMoveId, CancellationToken);
        moved.TodoListId.Should().NotBe(destinationListId, "a failed move must not have partially applied");
    }

    /// <summary>
    /// <c>MoveTodoListItemEndpoint</c> now verifies the destination <c>TodoList</c> exists from the caller's
    /// own (globally query-filtered) <c>DbContext</c> before writing <c>entity.TodoListId</c> -- a list
    /// belonging to another user simply doesn't resolve, giving the same 404 the rest of this API's IDOR
    /// matrix (group A) gives for a foreign id, rather than silently relocating the item.
    /// </summary>
    [Fact(DisplayName = "Moving an item to another user's list 404s and leaves the item untouched")]
    public async Task Move_ToAnotherUsersList_Returns404AndLeavesItemUnchanged()
    {
        long itemId, myOriginalListId;
        await using (var db = CreateDbContext())
        {
            var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "move-idor-activity");
            var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 701, text: "move-idor-priority");
            myOriginalListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, name: "move-idor-mine");
            itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, myOriginalListId);
        }

        long theirListId;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            theirListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ReminderSeedHelper.OtherUserId, "move-idor-theirs");
        }

        var response = await CreateClient().PatchAsJsonAsync(
            $"api/todo-list-item/{itemId}/move", new { DestinationListId = theirListId }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the destination-list lookup is scoped to the caller's UserId via the global query filter, so a " +
            "foreign DestinationListId resolves to nothing");

        await using var verifyDb = CreateDbContext();
        var item = await verifyDb.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.TodoListId.Should().Be(myOriginalListId, "a refused move must not have partially applied");
    }
}
