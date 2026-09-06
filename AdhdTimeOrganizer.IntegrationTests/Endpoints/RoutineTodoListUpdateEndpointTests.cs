using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// `PUT /api/routine-todo-list/{id}` — the three things it must not do to a routine item.
/// </summary>
/// <remarks>
/// <para>
/// <b>SEC-2.</b> `IsDone` / `DoneCount` / `DisplayOrder` ride in on the inherited request shape and used to
/// be written straight onto the entity. Completing a routine that way skips the toggle endpoint, so it
/// skips the item streak and `RoutineTodoListIsDoneChangedEvent` — the only channel by which Planning
/// learns a routine was completed — and leaves the three representations of "done" out of step. Nothing
/// errors: cross-slice handlers log and swallow.
/// </para>
/// <para>
/// <b>CQ-15.</b> `DisplayOrder` is per-period, so an item moved to another period has to be re-ordered
/// into its destination rather than carrying a number that means nothing there.
/// </para>
/// <para>
/// <b>CQ-4.</b> Steps are an owned collection serialised `ToJson`, with `Id` assigned in the entity and
/// `ValueGeneratedNever()` — so rebuilding the list from fresh instances mints a new `Guid` per step on
/// every save, and the three step routes that take a `{stepId:guid}` in the path 404 against steps the
/// user can still see.
/// </para>
/// </remarks>
[Collection("Postgres")]
public class RoutineTodoListUpdateEndpointTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    // ---- SEC-2: server-owned fields ----------------------------------------------------------------

    [Fact(DisplayName = "SEC-2: the PUT cannot complete a routine item or reposition it")]
    public async Task UpdateRoutineTodoList_CannotWriteIsDoneOrDisplayOrder()
    {
        long activityId, periodId, itemId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Server-owned fields", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new
            {
                activityId,
                timePeriodId = periodId,
                isDone = true,
                displayOrder = 5L,
                note = "edited"
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId);
        item.IsDone.Should().BeFalse("completion goes through the toggle endpoint, which is what raises the cross-slice event");
        item.DisplayOrder.Should().Be(1000, "display order is derived per period, not taken from the body");
        item.Note.Should().Be("edited", "the fields the PUT does own must still be written");
    }

    /// <summary>
    /// The counted-item companion: `DoneCount` is no longer client-supplied, so lowering `TotalCount` under
    /// the count already recorded has to clamp rather than either take the body's word for it or hit
    /// `CK_RoutineTodoList_DoneCount_LessOrEqual_TotalCount` and 400.
    /// </summary>
    [Fact(DisplayName = "SEC-2: lowering TotalCount clamps the server-owned DoneCount instead of failing")]
    public async Task UpdateRoutineTodoList_LoweringTotalCount_ClampsDoneCount()
    {
        long activityId, periodId, itemId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Counted item clamp", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
                db, activityId, periodId, totalCount: 5, doneCount: 3, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new
            {
                activityId,
                timePeriodId = periodId,
                isDone = false,
                displayOrder = 1000L,
                totalCount = 2,
                doneCount = 3
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId);
        item.TotalCount.Should().Be(2);
        item.DoneCount.Should().Be(2);
    }

    // ---- CQ-15: display order follows the period ---------------------------------------------------

    [Fact(DisplayName = "CQ-15: moving an item to another period re-derives its display order")]
    public async Task UpdateRoutineTodoList_MovedToAnotherPeriod_ReDerivesDisplayOrder()
    {
        long movedActivityId, sourcePeriodId, destinationPeriodId, itemId;
        await using (var db = CreateDbContext())
        {
            movedActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Moved routine", ct: CancellationToken);
            var sittingActivityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Routine already in the destination", ct: CancellationToken);

            // Different lengths: (UserId, LengthInDays) is unique on a period.
            sourcePeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 7, ct: CancellationToken);
            destinationPeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, lengthInDays: 14, ct: CancellationToken);

            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, movedActivityId, sourcePeriodId, ct: CancellationToken);
            // Both seeded items land on 1000, so the move has to move off it.
            await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, sittingActivityId, destinationPeriodId, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new
            {
                activityId = movedActivityId,
                timePeriodId = destinationPeriodId,
                isDone = false,
                displayOrder = 1000L
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var item = await ReadItemAsync(itemId);
        item.TimePeriodId.Should().Be(destinationPeriodId);
        item.DisplayOrder.Should().BeLessThan(1000,
            "the item is ordered into the destination the way the create path orders a new one — above the row already there");
    }

    /// <summary>
    /// The control: without it the re-derivation could be running on every save, which would silently undo
    /// whatever the reorder endpoint last decided.
    /// </summary>
    [Fact(DisplayName = "CQ-15: an edit that stays in the same period leaves the display order alone")]
    public async Task UpdateRoutineTodoList_SamePeriod_KeepsDisplayOrder()
    {
        long activityId, periodId, itemId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Display order untouched", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new { activityId, timePeriodId = periodId, isDone = false, displayOrder = 1000L, note = "still here" },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadItemAsync(itemId)).DisplayOrder.Should().Be(1000);
    }

    // ---- CQ-4: step identity -----------------------------------------------------------------------

    [Fact(DisplayName = "CQ-4: a step keeps its Guid and its IsDone across a PUT of its parent item")]
    public async Task UpdateRoutineTodoList_ReconcilesStepsInPlace()
    {
        var doneStep = new TodoListStep { Name = "Step one", Order = 1, IsDone = true };
        var openStep = new TodoListStep { Name = "Step two", Order = 2 };

        long activityId, periodId, itemId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Step identity", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
                db, activityId, periodId, steps: [doneStep, openStep], ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new
            {
                activityId,
                timePeriodId = periodId,
                isDone = false,
                displayOrder = 1000L,
                steps = new object[]
                {
                    new { id = doneStep.Id, name = "Step one, renamed", order = 1, note = "with a note now" },
                    new { id = (Guid?)null, name = "Step three", order = 3, note = (string?)null }
                }
            },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var steps = (await ReadItemAsync(itemId)).Steps.OrderBy(s => s.Order).ToList();
        steps.Should().HaveCount(2, "the step the request omitted is gone");

        steps[0].Id.Should().Be(doneStep.Id, "three step routes take this id in the path");
        steps[0].IsDone.Should().BeTrue("the tick survives an edit of its parent");
        steps[0].Name.Should().Be("Step one, renamed");
        steps[0].Note.Should().Be("with a note now");

        steps[1].Id.Should().NotBeEmpty("a step added without an id still gets one");
        steps[1].Id.Should().NotBe(openStep.Id);
        steps[1].IsDone.Should().BeFalse();
    }

    [Fact(DisplayName = "CQ-4: omitting Steps entirely leaves them untouched")]
    public async Task UpdateRoutineTodoList_WithoutSteps_LeavesThemAlone()
    {
        var step = new TodoListStep { Name = "Untouched", Order = 1, IsDone = true };

        long activityId, periodId, itemId;
        await using (var db = CreateDbContext())
        {
            activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, "Steps omitted", ct: CancellationToken);
            periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(
                db, activityId, periodId, steps: [step], ct: CancellationToken);
        }

        var response = await CreateUserRoleClient().PutAsJsonAsync(
            $"/api/routine-todo-list/{itemId}",
            new { activityId, timePeriodId = periodId, isDone = false, displayOrder = 1000L, note = "edited" },
            JsonOpts,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var steps = (await ReadItemAsync(itemId)).Steps.ToList();
        steps.Should().ContainSingle();
        steps[0].Id.Should().Be(step.Id);
        steps[0].IsDone.Should().BeTrue();
        steps[0].Name.Should().Be("Untouched");
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private async Task<RoutineTodoList> ReadItemAsync(long itemId)
    {
        await using var db = CreateDbContext();
        return await db.Set<RoutineTodoList>().SingleAsync(r => r.Id == itemId, CancellationToken);
    }
}
