using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.application.service;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO3 phase 1: <c>TodoListItemResponse.PlanHelp</c>, a to-do item's planner attempts read back through
/// <c>ITodoListItemPlanSource.PlanHistoryAsync</c> and turned into one kind of help. Before this the next-slot
/// read dropped every block that had already happened, so an item carried over four times looked new.
/// <para>
/// Like the next-slot read, this seam fails <b>silently</b>. An implementation that drops a status or the
/// day predicate answers 200 with every item stuck, or none ever. So each case asserts on the token that comes
/// back for a specific history. The rule's own edges (threshold, ties) are pinned without a database in
/// <c>Services.TodoListItemPlanHelpRuleTests</c>; this file pins that the seam sorts real rows into it.
/// </para>
/// <para><b>Rank band 480–489</b>, see <c>FilterSortTodoListTests</c> for why every file claims one.</para>
/// </summary>
[Collection("Postgres")]
public class TodoListItemPlanHelpTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    // The fixture user is on UTC, so the user's today is UTC's.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private long _listId;
    private long _priorityId;

    protected override async Task SeedAsync(DbContext db)
    {
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 480, UserId, "plan-help", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Plan help list", ct: CancellationToken);
    }

    [Fact(DisplayName = "FO3: carried over, missed and skipped blocks each count, and the list read offers help by the dominant reason")]
    public async Task ListRead_SortsEachItemsAttemptsIntoTheRightHelp()
    {
        // Carried over twice and now waiting tomorrow, plus one block from yesterday nobody started: three slips
        // with no reason given.
        var (slipping, slippingActivity) = await SeedItemAsync("slipping");
        await SeedLinkedTaskAsync(slippingActivity, slipping, Today.AddDays(1), carryOverCount: 2);
        await SeedLinkedTaskAsync(slippingActivity, slipping, Today.AddDays(-1));

        var (noTime, noTimeActivity) = await SeedItemAsync("no-time");
        await SeedLinkedTaskAsync(noTimeActivity, noTime, Today.AddDays(-3), PlannerTaskStatus.Cancelled, SkipReasonKey.NoTime);
        await SeedLinkedTaskAsync(noTimeActivity, noTime, Today.AddDays(-2), PlannerTaskStatus.Cancelled, SkipReasonKey.NoTime);
        await SeedLinkedTaskAsync(noTimeActivity, noTime, Today.AddDays(-1));

        var (notRelevant, notRelevantActivity) = await SeedItemAsync("not-relevant");
        await SeedLinkedTaskAsync(notRelevantActivity, notRelevant, Today.AddDays(-3), PlannerTaskStatus.Cancelled, SkipReasonKey.NotRelevant);
        await SeedLinkedTaskAsync(notRelevantActivity, notRelevant, Today.AddDays(-2), PlannerTaskStatus.Cancelled, SkipReasonKey.NotRelevant);
        await SeedLinkedTaskAsync(notRelevantActivity, notRelevant, Today.AddDays(-1), PlannerTaskStatus.Cancelled, SkipReasonKey.NoTime);

        var items = await ReadListAsync();

        items.Single(i => i.Id == slipping).PlanHelp.Should().Be(TodoListItemPlanHelp.SmallerStep,
            "each carry-over is a day the item did not happen on, and the missed block is a third");
        items.Single(i => i.Id == slipping).NextPlannedSlot.Should().NotBeNull(
            "the help sits beside the schedule; tomorrow's block is still the next slot");
        items.Single(i => i.Id == noTime).PlanHelp.Should().Be(TodoListItemPlanHelp.ShorterBlock);
        items.Single(i => i.Id == notRelevant).PlanHelp.Should().Be(TodoListItemPlanHelp.Reconsider);
    }

    [Fact(DisplayName = "FO3: a completed attempt, a done item, or fewer than three slips means no help is offered")]
    public async Task ListRead_OffersNoHelpWhenTheItemIsNotStuck()
    {
        var (onceDone, onceDoneActivity) = await SeedItemAsync("once-completed");
        await SeedLinkedTaskAsync(onceDoneActivity, onceDone, Today.AddDays(-4), carryOverCount: 3);
        await SeedLinkedTaskAsync(onceDoneActivity, onceDone, Today.AddDays(-2), PlannerTaskStatus.Completed);

        var (doneItem, doneActivity) = await SeedItemAsync("done-item", isDone: true);
        await SeedLinkedTaskAsync(doneActivity, doneItem, Today.AddDays(-1), carryOverCount: 3);

        var (twoSlips, twoSlipsActivity) = await SeedItemAsync("two-slips");
        await SeedLinkedTaskAsync(twoSlipsActivity, twoSlips, Today.AddDays(-2));
        await SeedLinkedTaskAsync(twoSlipsActivity, twoSlips, Today.AddDays(-1), PlannerTaskStatus.Cancelled);

        var (unplanned, _) = await SeedItemAsync("never-planned");

        var items = await ReadListAsync();

        items.Single(i => i.Id == onceDone).PlanHelp.Should().BeNull("it has happened once, so it is not a can't-start item");
        items.Single(i => i.Id == doneItem).PlanHelp.Should().BeNull("a done item needs no help starting");
        items.Single(i => i.Id == twoSlips).PlanHelp.Should().BeNull("two slips is below the threshold");
        items.Single(i => i.Id == unplanned).PlanHelp.Should().BeNull();
    }

    /// <summary>
    /// The day predicate. A block planned for today has not been missed yet, and a background block runs
    /// alongside the day rather than being an attempt at it. Counting either would make this item stuck.
    /// </summary>
    [Fact(DisplayName = "FO3: today's unstarted block and a background block are not slips")]
    public async Task ListRead_DoesNotCountTodayOrBackgroundBlocks()
    {
        var (item, activity) = await SeedItemAsync("today-and-background");
        await SeedLinkedTaskAsync(activity, item, Today.AddDays(-2));
        await SeedLinkedTaskAsync(activity, item, Today.AddDays(-1));
        await SeedLinkedTaskAsync(activity, item, Today);
        await SeedLinkedTaskAsync(activity, item, Today.AddDays(-1), isBackground: true);

        (await ReadListAsync()).Single(i => i.Id == item).PlanHelp.Should().BeNull();
    }

    /// <summary>
    /// End to end through the real write path: three carry-overs via <c>POST /planner-task/{id}/carry-over</c>,
    /// not a seeded count. The wire spelling and the absence of any count field are asserted on the raw JSON.
    /// FO3's one rule is that the number is never shown, and a typed deserialize would not notice an extra field.
    /// </summary>
    [Fact(DisplayName = "FO3: three real carry-overs make the item stuck, and the response carries the kind, never a count")]
    public async Task ByIdRead_AfterThreeCarryOvers_CarriesOnlyTheKind()
    {
        var (item, activity) = await SeedItemAsync("carried-thrice");
        var taskId = await SeedLinkedTaskAsync(activity, item, Today.AddDays(-1));

        var client = CreateClient();
        foreach (var offset in new[] { 0, 1, 2 })
        {
            var carry = await client.PostAsJsonAsync($"/api/planner-task/{taskId}/carry-over",
                new { Date = Today.AddDays(offset).ToString("yyyy-MM-dd") }, JsonOpts, CancellationToken);
            carry.StatusCode.Should().Be(HttpStatusCode.OK, await carry.Content.ReadAsStringAsync(CancellationToken));
        }

        var response = await client.GetAsync($"/api/todo-list-item/{item}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("planHelp").GetString().Should().Be("smallerStep");
        json.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("carr", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("attempt", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("slip", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("postpone", StringComparison.OrdinalIgnoreCase),
                "the help is offered without the score that led to it");
    }

    [Fact(DisplayName = "FO3: the home widget's rows carry the same help as the list")]
    public async Task DashboardWidget_CarriesPlanHelp()
    {
        var (item, activity) = await SeedItemAsync("due-and-stuck", dueDate: Today);
        await SeedLinkedTaskAsync(activity, item, Today.AddDays(-1), carryOverCount: 3);

        var response = await CreateClient().GetAsync("/api/todo-list-item/dashboard-widget", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        JsonSerializer.Deserialize<List<TodoListItemResponse>>(body, JsonOpts)!
            .Single(i => i.Id == item).PlanHelp.Should().Be(TodoListItemPlanHelp.SmallerStep);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>One activity per item: <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>.</summary>
    private async Task<(long ItemId, long ActivityId)> SeedItemAsync(string name, bool isDone = false, DateOnly? dueDate = null)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);
        var itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, _priorityId, _listId, UserId, isDone: isDone, ct: CancellationToken, dueDate: dueDate);
        return (itemId, activityId);
    }

    /// <summary>
    /// The skip reason is written straight onto the row: <c>ApplyStatus</c> is the write path's rule, and here
    /// the row is the input.
    /// </summary>
    private async Task<long> SeedLinkedTaskAsync(
        long activityId, long todoListItemId, DateOnly date,
        PlannerTaskStatus status = PlannerTaskStatus.NotStarted, SkipReasonKey? skipReasonKey = null,
        int carryOverCount = 0, bool isBackground = false)
    {
        await using var db = CreateDbContext();
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(10, 0), new TimeOnly(10, 30), UserId, status, ct: CancellationToken);

        var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.TodolistItemId = todoListItemId;
        task.SkipReasonKey = skipReasonKey;
        task.CarryOverCount = carryOverCount;
        task.IsBackground = isBackground;
        await db.SaveChangesAsync(CancellationToken);
        return taskId;
    }

    private async Task<List<TodoListItemResponse>> ReadListAsync()
    {
        var response = await CreateClient().GetAsync($"/api/todo-list-item?todoListId={_listId}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<List<TodoListItemResponse>>(body, JsonOpts)!;
    }
}
