using System.Net;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.application.dto.dto;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// <c>TodoListItemResponse.NextPlannedSlot</c> — "is this to-do scheduled?", read from the planner through
/// <c>ITodoListItemPlanSource</c> rather than inferred from <c>DueDate</c>. The deadline and the schedule used to
/// share that one field: planning a block stamped its date into an empty <c>DueDate</c>, so a missed block made the
/// to-do overdue and a moved one left its time stale (bugs-to-investigate #8).
/// <para>
/// A seam that fails to resolve throws, but one that answers wrongly is silent — every item would simply read as
/// unscheduled, or scheduled forever — so this asserts on what comes back, per the backend's silent-failure rule.
/// </para>
/// <para><b>Rank band 470–479</b>, see <c>GetAllTodoListItemTests</c> for why every file claims one.</para>
/// </summary>
[Collection("Postgres")]
public class TodoListItemNextPlannedSlotTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // The fixture user is on UTC, so the user's today is UTC's.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private long _listId;
    private long _plannedItemId;
    private long _closedOrPastItemId;
    private long _unplannedItemId;

    protected override async Task SeedAsync(DbContext db)
    {
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 470, UserId, "planned-slot", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Planned slot list", ct: CancellationToken);

        var plannedActivity = await PlanningTestSeedHelper.SeedActivityAsync(db, "planned-item", UserId, CancellationToken);
        var closedActivity = await PlanningTestSeedHelper.SeedActivityAsync(db, "closed-or-past-item", UserId, CancellationToken);
        var unplannedActivity = await PlanningTestSeedHelper.SeedActivityAsync(db, "unplanned-item", UserId, CancellationToken);

        _plannedItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, plannedActivity, priorityId, _listId, UserId, ct: CancellationToken);
        _closedOrPastItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, closedActivity, priorityId, _listId, UserId, ct: CancellationToken);
        _unplannedItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, unplannedActivity, priorityId, _listId, UserId, ct: CancellationToken);

        // Two open blocks for the planned item, the later one seeded first: the earlier one must win.
        await SeedLinkedTaskAsync(db, plannedActivity, _plannedItemId, Today.AddDays(2), new TimeOnly(9, 0));
        await SeedLinkedTaskAsync(db, plannedActivity, _plannedItemId, Today.AddDays(1), new TimeOnly(14, 0));

        // None of these is "scheduled": a completed block, a skipped one, and yesterday's block nobody did.
        await SeedLinkedTaskAsync(db, closedActivity, _closedOrPastItemId, Today.AddDays(1), new TimeOnly(10, 0), PlannerTaskStatus.Completed);
        await SeedLinkedTaskAsync(db, closedActivity, _closedOrPastItemId, Today.AddDays(1), new TimeOnly(11, 0), PlannerTaskStatus.Cancelled);
        await SeedLinkedTaskAsync(db, closedActivity, _closedOrPastItemId, Today.AddDays(-1), new TimeOnly(10, 0));
    }

    [Fact(DisplayName = "The list read reports each item's earliest open planned block, and only open, current ones")]
    public async Task ListRead_ReportsTheNextOpenPlannedSlot()
    {
        var response = await CreateClient().GetAsync($"/api/todo-list-item?todoListId={_listId}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var items = JsonSerializer.Deserialize<List<TodoListItemResponse>>(body, JsonOpts)!;

        var planned = items.Single(i => i.Id == _plannedItemId);
        planned.NextPlannedSlot.Should().BeEquivalentTo(new PlannedSlotResponse { Date = Today.AddDays(1), StartTime = new TimeDto(14, 0) });
        planned.DueDate.Should().BeNull("the planner reports the schedule; it never writes the user's deadline");

        items.Single(i => i.Id == _closedOrPastItemId).NextPlannedSlot.Should().BeNull(
            "a done block, a skipped block and a missed one from yesterday all leave the item unscheduled");
        items.Single(i => i.Id == _unplannedItemId).NextPlannedSlot.Should().BeNull();
    }

    [Fact(DisplayName = "The single-item read carries the same planned slot as the list read")]
    public async Task ByIdRead_ReportsTheNextOpenPlannedSlot()
    {
        var response = await CreateClient().GetAsync($"/api/todo-list-item/{_plannedItemId}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        JsonSerializer.Deserialize<TodoListItemResponse>(body, JsonOpts)!
            .NextPlannedSlot.Should().BeEquivalentTo(new PlannedSlotResponse { Date = Today.AddDays(1), StartTime = new TimeDto(14, 0) });
    }

    private async Task SeedLinkedTaskAsync(
        DbContext db, long activityId, long todoListItemId, DateOnly date, TimeOnly start,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted)
    {
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, start, start.AddMinutes(30), UserId, status, ct: CancellationToken);

        var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.TodolistItemId = todoListItemId;
        await db.SaveChangesAsync(CancellationToken);
    }
}
