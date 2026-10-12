using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.IntegrationTests.Reminders;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO3 phase 2 on the to-do side: <c>POST /todo-list-item/reschedule-overdue</c>, which replaced the renegotiation
/// banner's PUT-per-item loop, and the item PUT. Both feed <c>TodoListItemPostponeRule</c>, and an item pushed
/// three times is offered help on the list read without the count reaching the wire.
/// <para>The fixture user is on UTC, so the user's today is UTC's.</para>
/// <para><b>Rank band 490–499</b>, see <c>FilterSortTodoListTests</c> for why every file claims one.</para>
/// </summary>
[Collection("Postgres")]
public class RescheduleOverdueTodoListItemTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private long _listId;
    private long _otherListId;
    private long _priorityId;

    protected override async Task SeedAsync(DbContext db)
    {
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 490, UserId, "reschedule", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Reschedule list", ct: CancellationToken);
        _otherListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Other list", ct: CancellationToken);
    }

    [Fact(DisplayName = "FO3: the list's open overdue items move to today + days, each recorded as a postponement")]
    public async Task Reschedule_MovesOnlyTheListsOpenOverdueItems()
    {
        var overdue = await SeedItemAsync("overdue", Today.AddDays(-3));
        var dueToday = await SeedItemAsync("due-today", Today);
        var future = await SeedItemAsync("future", Today.AddDays(2));
        var done = await SeedItemAsync("done-overdue", Today.AddDays(-3), isDone: true);
        var undated = await SeedItemAsync("undated", null);
        var otherList = await SeedItemAsync("other-list", Today.AddDays(-3), listId: _otherListId);

        var result = await RescheduleAsync(_listId, 7);

        result.DueDate.Should().Be(Today.AddDays(7), "measured from today, not from each item's own date");
        result.MovedIds.Should().BeEquivalentTo([overdue]);

        var moved = await LoadAsync(overdue);
        moved.DueDate.Should().Be(Today.AddDays(7));
        moved.PostponeCount.Should().Be(1);
        moved.FirstDueDate.Should().Be(Today.AddDays(-3));

        (await LoadAsync(dueToday)).DueDate.Should().Be(Today, "due today is not overdue yet");
        (await LoadAsync(future)).DueDate.Should().Be(Today.AddDays(2));
        (await LoadAsync(done)).DueDate.Should().Be(Today.AddDays(-3), "a done item is not part of the pile");
        (await LoadAsync(undated)).DueDate.Should().BeNull();
        (await LoadAsync(otherList)).DueDate.Should().Be(Today.AddDays(-3), "only the named list moves");
    }

    [Fact(DisplayName = "FO3: with no list id every list's overdue items move, and another user's never do")]
    public async Task Reschedule_WithoutAList_MovesEveryListOfTheCaller()
    {
        var first = await SeedItemAsync("first", Today.AddDays(-1));
        var second = await SeedItemAsync("second", Today.AddDays(-5), listId: _otherListId);

        long strangers;
        await using (var db = CreateDbContext())
        {
            await ReminderSeedHelper.EnsureOtherUserAsync(db, CancellationToken);
            var otherUser = ReminderSeedHelper.OtherUserId;
            var priority = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 490, otherUser, ct: CancellationToken);
            var list = await TodoListTestSeedHelper.SeedTodoListAsync(db, otherUser, ct: CancellationToken);
            var activity = await PlanningTestSeedHelper.SeedActivityAsync(db, "strangers", otherUser, CancellationToken);
            strangers = await TodoListTestSeedHelper.SeedTodoListItemAsync(
                db, activity, priority, list, otherUser, ct: CancellationToken, dueDate: Today.AddDays(-2));
        }

        var result = await RescheduleAsync(null, 0);

        result.MovedIds.Should().BeEquivalentTo([first, second]);
        (await LoadAsync(first)).DueDate.Should().Be(Today);
        (await LoadAsync(second)).DueDate.Should().Be(Today);
        (await LoadAsync(strangers)).DueDate.Should().Be(Today.AddDays(-2));
    }

    [Theory(DisplayName = "FO3: days outside 0–365 and a non-positive list id are refused")]
    [InlineData(-1, null)]
    [InlineData(366, null)]
    [InlineData(0, 0L)]
    public async Task Reschedule_InvalidBody_Is400(int days, long? listId)
    {
        var overdue = await SeedItemAsync("untouched", Today.AddDays(-1));

        var response = await CreateClient().PostAsJsonAsync("api/todo-list-item/reschedule-overdue",
            new { ListId = listId, Days = days }, JsonOpts, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoadAsync(overdue)).DueDate.Should().Be(Today.AddDays(-1));
    }

    [Fact(DisplayName = "FO3: the item PUT counts pushing an overdue date later, and not moving a date that has not come")]
    public async Task Put_CountsOnlyAnOverduePush()
    {
        var overdue = await SeedItemAsync("put-overdue", Today.AddDays(-2));
        var upcoming = await SeedItemAsync("put-upcoming", Today.AddDays(1));

        await PutDueDateAsync(overdue, Today.AddDays(1));
        await PutDueDateAsync(upcoming, Today.AddDays(4));

        var pushed = await LoadAsync(overdue);
        pushed.PostponeCount.Should().Be(1);
        pushed.FirstDueDate.Should().Be(Today.AddDays(-2));

        var planned = await LoadAsync(upcoming);
        planned.PostponeCount.Should().Be(0);
        planned.FirstDueDate.Should().BeNull();
    }

    /// <summary>
    /// End to end: an item never put on the planner, pushed three times by the banner, gets the same help a
    /// planner-slipping item does — and the raw JSON carries the kind, never the count.
    /// </summary>
    [Fact(DisplayName = "FO3: an item renegotiated three times is offered a smaller step, without the count on the wire")]
    public async Task ThreeRenegotiations_OfferASmallerStep_WithoutACount()
    {
        var item = await SeedItemAsync("renegotiated", Today.AddDays(-1));

        await RescheduleAsync(_listId, 0);
        await BackdateAsync(item, Today.AddDays(-1));
        await RescheduleAsync(_listId, 0);
        await BackdateAsync(item, Today.AddDays(-1));
        await RescheduleAsync(_listId, 0);

        var response = await CreateClient().GetAsync($"api/todo-list-item?todoListId={_listId}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(body);
        var row = json.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetInt64() == item);
        row.GetProperty("planHelp").GetString().Should().Be("smallerStep");
        row.EnumerateObject().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("postpone", StringComparison.OrdinalIgnoreCase)
                                         || name.Contains("firstDue", StringComparison.OrdinalIgnoreCase),
                "the help is offered without the score that led to it");
    }

    [Fact(DisplayName = "FO3: undo puts the dates back and takes the postponement back with them")]
    public async Task Undo_RestoresDatesAndTheSlipRecord()
    {
        var firstSlip = await SeedItemAsync("undo-first-slip", Today.AddDays(-3));
        var slippedBefore = await SeedItemAsync("undo-second-slip", Today.AddDays(-1));
        await SetSlipAsync(slippedBefore, postponeCount: 1, firstDueDate: Today.AddDays(-4));

        var result = await RescheduleAsync(_listId, 7);
        result.UndoToken.Should().NotBeNullOrEmpty();
        (await LoadAsync(firstSlip)).PostponeCount.Should().Be(1);
        (await LoadAsync(slippedBefore)).PostponeCount.Should().Be(2);

        var restored = await UndoAsync(result.UndoToken!);

        restored.RestoredIds.Should().BeEquivalentTo([firstSlip, slippedBefore]);

        var first = await LoadAsync(firstSlip);
        first.DueDate.Should().Be(Today.AddDays(-3));
        first.PostponeCount.Should().Be(0, "the move the user took back is not a slip");
        first.FirstDueDate.Should().BeNull("it was set by the move that was taken back");

        var second = await LoadAsync(slippedBefore);
        second.DueDate.Should().Be(Today.AddDays(-1));
        second.PostponeCount.Should().Be(1, "only the undone move is taken back, not the earlier one");
        second.FirstDueDate.Should().Be(Today.AddDays(-4));
    }

    [Fact(DisplayName = "FO3: undo leaves an item re-dated or ticked off since as the user left it")]
    public async Task Undo_SkipsItemsChangedSince()
    {
        var redated = await SeedItemAsync("undo-redated", Today.AddDays(-2));
        var ticked = await SeedItemAsync("undo-ticked", Today.AddDays(-2));
        var untouched = await SeedItemAsync("undo-untouched", Today.AddDays(-2));
        var result = await RescheduleAsync(_listId, 0);

        await BackdateAsync(redated, Today.AddDays(3));
        await using (var db = CreateDbContext())
        {
            var item = await db.Set<TodoListItem>().IgnoreQueryFilters().SingleAsync(i => i.Id == ticked, CancellationToken);
            item.IsDone = true;
            await db.SaveChangesAsync(CancellationToken);
        }

        var restored = await UndoAsync(result.UndoToken!);

        restored.RestoredIds.Should().BeEquivalentTo([untouched]);
        var keptRedated = await LoadAsync(redated);
        keptRedated.DueDate.Should().Be(Today.AddDays(3));
        keptRedated.PostponeCount.Should().Be(1);
        (await LoadAsync(ticked)).DueDate.Should().Be(Today);
        (await LoadAsync(untouched)).DueDate.Should().Be(Today.AddDays(-2));
    }

    [Fact(DisplayName = "FO3: a spent or unknown undo token is a 404 and restores nothing")]
    public async Task Undo_SpentOrUnknownToken_Is404()
    {
        var item = await SeedItemAsync("undo-twice", Today.AddDays(-2));
        var result = await RescheduleAsync(_listId, 0);
        await UndoAsync(result.UndoToken!);
        await RescheduleAsync(_listId, 0);

        var again = await CreateClient().PostAsJsonAsync("api/todo-list-item/reschedule-overdue/undo",
            new { UndoToken = result.UndoToken }, JsonOpts, CancellationToken);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LoadAsync(item)).DueDate.Should().Be(Today, "the second renegotiation stands");

        var unknown = await CreateClient().PostAsJsonAsync("api/todo-list-item/reschedule-overdue/undo",
            new { UndoToken = Guid.NewGuid().ToString("N") }, JsonOpts, CancellationToken);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "FO3: nothing overdue moves nothing and offers no undo")]
    public async Task Reschedule_NothingOverdue_HasNoUndoToken()
    {
        await SeedItemAsync("not-overdue", Today.AddDays(1));

        var result = await RescheduleAsync(_listId, 0);

        result.MovedIds.Should().BeEmpty();
        result.UndoToken.Should().BeNull();
    }

    // ---------------------------------------------------------------- helpers

    private async Task<UndoRescheduleOverdueTodoListItemResponse> UndoAsync(string token)
    {
        var response = await CreateClient().PostAsJsonAsync("api/todo-list-item/reschedule-overdue/undo",
            new { UndoToken = token }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<UndoRescheduleOverdueTodoListItemResponse>(body, JsonOpts)!;
    }

    private async Task SetSlipAsync(long itemId, int postponeCount, DateOnly firstDueDate)
    {
        await using var db = CreateDbContext();
        var item = await db.Set<TodoListItem>().IgnoreQueryFilters().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.PostponeCount = postponeCount;
        item.FirstDueDate = firstDueDate;
        await db.SaveChangesAsync(CancellationToken);
    }

    /// <summary>One activity per item: <c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>.</summary>
    private async Task<long> SeedItemAsync(string name, DateOnly? dueDate, bool isDone = false, long? listId = null)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"{name}-{Guid.NewGuid():N}", UserId, CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, _priorityId, listId ?? _listId, UserId, isDone: isDone, ct: CancellationToken, dueDate: dueDate);
    }

    private async Task<RescheduleOverdueTodoListItemResponse> RescheduleAsync(long? listId, int days)
    {
        var response = await CreateClient().PostAsJsonAsync("api/todo-list-item/reschedule-overdue",
            new { ListId = listId, Days = days }, JsonOpts, CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<RescheduleOverdueTodoListItemResponse>(body, JsonOpts)!;
    }

    private async Task PutDueDateAsync(long itemId, DateOnly dueDate)
    {
        var item = await LoadAsync(itemId);
        var response = await CreateClient().PutAsJsonAsync($"api/todo-list-item/{itemId}", new
        {
            activityId = item.ActivityId,
            taskPriorityId = item.TaskPriorityId,
            isDone = false,
            dueDate
        }, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    /// <summary>The day passing, simulated: the item is due yesterday again, as it would be a day later.</summary>
    private async Task BackdateAsync(long itemId, DateOnly dueDate)
    {
        await using var db = CreateDbContext();
        var item = await db.Set<TodoListItem>().IgnoreQueryFilters().SingleAsync(i => i.Id == itemId, CancellationToken);
        item.DueDate = dueDate;
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<TodoListItem> LoadAsync(long itemId)
    {
        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == itemId, CancellationToken);
    }
}
