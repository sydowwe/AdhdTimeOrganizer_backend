using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.Core.domain.rule;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.TodoLists.application.dto.response.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// F3-01: <c>GET /todo-list-item/estimate-fit</c>, how to-do estimates compared with the time logged against each
/// item and with the block it was first planned into.
/// <para>
/// Both sides cross a slice through a seam, and both fail <b>silently</b>: keyed on the activity instead of the
/// item, the logged side would still answer 200 with a plausible ratio; reading the latest block instead of the
/// first, the planned side would too. So every case asserts on the figure that comes back for a seeded history.
/// </para>
/// <para><b>Rank band 520–529</b>, see <c>FilterSortTodoListTests</c> for why every file claims one.</para>
/// </summary>
[Collection("Postgres")]
public class TodoListItemEstimateFitTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;
    private const int ThirtyMinutes = 30 * 60;

    private long _listId;
    private long _otherListId;
    private long _priorityId;
    private long _otherPriorityId;
    private long _categoryId;

    protected override async Task SeedAsync(DbContext db)
    {
        _priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 520, UserId, "fit-normal", CancellationToken);
        _otherPriorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 521, UserId, "fit-high", CancellationToken);
        _categoryId = await TodoListTestSeedHelper.SeedTodoListCategoryAsync(db, UserId, "Fit category", CancellationToken);
        _listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Admin", _categoryId, CancellationToken);
        _otherListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, UserId, "Writing", ct: CancellationToken);
    }

    [Fact(DisplayName = "F3-01: the group's figure is the median of logged ÷ estimate, so one runaway item does not move it")]
    public async Task LoggedRatio_IsTheMedianPerItem()
    {
        // Logged 15, 30, 45, 60 and 300 minutes against 30-minute estimates: ratios 0.5, 1, 1.5, 2 and 10.
        foreach (var minutes in new[] { 15, 30, 45, 60, 300 })
            await SeedCompletedItemAsync($"median-{minutes}", loggedSeconds: minutes * 60);

        var fit = await FitAsync("list");

        var group = fit.Groups.Should().ContainSingle().Subject;
        group.Key.Should().Be(_listId);
        group.LoggedRatio.Should().Be(1.5, "the middle of five ratios; a mean would say 2.9");
        group.SampleCount.Should().Be(5);
    }

    [Fact(DisplayName = "F3-01: below five items a group is absent, not reported with a weak figure")]
    public async Task Group_BelowTheThreshold_IsAbsent()
    {
        for (var i = 0; i < EstimateFitRule.TodoItemGroupMinimumSamples - 1; i++)
            await SeedCompletedItemAsync($"thin-{i}", loggedSeconds: 3600);

        var fit = await FitAsync("list");

        fit.Groups.Should().BeEmpty();
        fit.Items.Should().HaveCount(EstimateFitRule.TodoItemGroupMinimumSamples - 1,
            "the per-item figures are facts about one item and need no threshold");
    }

    /// <summary>
    /// The reason the item link exists. A heartbeat-attributed row on the same activity carries no item id, and
    /// matching on the activity would have credited it to the item.
    /// </summary>
    [Fact(DisplayName = "F3-01: only time logged against the item counts, not time on its activity")]
    public async Task LoggedTime_IsKeyedOnTheItem_NotTheActivity()
    {
        long firstActivity = 0;
        for (var i = 0; i < 5; i++)
        {
            var (_, activityId) = await SeedCompletedItemAsync($"keyed-{i}", loggedSeconds: ThirtyMinutes);
            if (i == 0) firstActivity = activityId;
        }

        await LogAsync(firstActivity, null, DateTime.UtcNow.AddHours(-2), 4 * 3600);

        var group = (await FitAsync("list")).Groups.Should().ContainSingle().Subject;
        group.LoggedRatio.Should().Be(1.0);
    }

    [Fact(DisplayName = "F3-01: an open item shows its time so far but never counts towards a group")]
    public async Task OpenItem_IsReported_ButNotSampled()
    {
        for (var i = 0; i < 4; i++)
            await SeedCompletedItemAsync($"done-{i}", loggedSeconds: ThirtyMinutes);
        var (openId, _) = await SeedItemAsync("still-open", isDone: false, loggedSeconds: 50 * 60);

        var fit = await FitAsync("list");

        fit.Groups.Should().BeEmpty("four completed items, and the open one's figure is not final");
        fit.Items.Should().ContainSingle(i => i.ItemId == openId).Which.LoggedSeconds.Should().Be(50 * 60);
    }

    [Fact(DisplayName = "F3-01: completions older than the window, items without an estimate and untimed items are left out")]
    public async Task Samples_AreTimedEstimatedRecentCompletions()
    {
        for (var i = 0; i < 4; i++)
            await SeedCompletedItemAsync($"recent-{i}", loggedSeconds: ThirtyMinutes);

        var (old, _) = await SeedCompletedItemAsync("too-old", loggedSeconds: ThirtyMinutes,
            completedAt: DateTime.UtcNow.AddDays(-91));
        var (unestimated, _) = await SeedCompletedItemAsync("no-estimate", loggedSeconds: ThirtyMinutes, suggestedSeconds: null);
        var (untimed, _) = await SeedCompletedItemAsync("never-timed", loggedSeconds: 0);

        var fit = await FitAsync("list");

        fit.Groups.Should().BeEmpty("only four items qualify");
        fit.Items.Select(i => i.ItemId).Should().NotContain([old, unestimated, untimed]);
    }

    [Fact(DisplayName = "F3-01: the planned figure reads each item's first block, and needs five planned items")]
    public async Task PlannedRatio_ReadsTheFirstBlock()
    {
        for (var i = 0; i < 5; i++)
        {
            var (itemId, activityId) = await SeedCompletedItemAsync($"planned-{i}", loggedSeconds: ThirtyMinutes);
            // First a 30-minute block, then a 2-hour one: only the first is the plan for the estimate.
            await SeedBlockAsync(activityId, itemId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3), new TimeOnly(9, 0), new TimeOnly(9, 30));
            await SeedBlockAsync(activityId, itemId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), new TimeOnly(9, 0), new TimeOnly(11, 0));
        }

        var group = (await FitAsync("list")).Groups.Should().ContainSingle().Subject;
        group.PlannedRatio.Should().Be(1.0);
        group.PlannedSampleCount.Should().Be(5);
    }

    [Fact(DisplayName = "F3-01: fewer than five planned items leave the planned figure null beside a logged one")]
    public async Task PlannedRatio_BelowTheThreshold_IsNull()
    {
        for (var i = 0; i < 5; i++)
        {
            var (itemId, activityId) = await SeedCompletedItemAsync($"part-planned-{i}", loggedSeconds: ThirtyMinutes);
            if (i < 2)
                await SeedBlockAsync(activityId, itemId, DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(9, 0), new TimeOnly(10, 0));
        }

        var group = (await FitAsync("list")).Groups.Should().ContainSingle().Subject;
        group.PlannedRatio.Should().BeNull();
        group.PlannedSampleCount.Should().Be(2);
    }

    [Fact(DisplayName = "F3-01: groupBy picks the key — list, the list's category (null when it has none), or priority")]
    public async Task GroupBy_PicksTheKey()
    {
        for (var i = 0; i < 5; i++)
            await SeedCompletedItemAsync($"admin-{i}", loggedSeconds: ThirtyMinutes);
        for (var i = 0; i < 5; i++)
            await SeedCompletedItemAsync($"writing-{i}", loggedSeconds: 2 * ThirtyMinutes, listId: _otherListId, priorityId: _otherPriorityId);

        var byList = await FitAsync("list");
        byList.Groups.Should().HaveCount(2);
        byList.Groups.Single(g => g.Key == _listId).LoggedRatio.Should().Be(1.0);
        byList.Groups.Single(g => g.Key == _otherListId).LoggedRatio.Should().Be(2.0);

        var byCategory = await FitAsync("Category");
        byCategory.Groups.Select(g => g.Key).Should().BeEquivalentTo(new long?[] { _categoryId, null });

        var byPriority = await FitAsync("PRIORITY");
        byPriority.Groups.Single(g => g.Key == _otherPriorityId).LoggedRatio.Should().Be(2.0);
    }

    [Fact]
    public async Task EstimateFit_DoesNotLeakAnotherUsersItems()
    {
        for (var i = 0; i < 5; i++)
            await SeedCompletedItemAsync($"mine-{i}", loggedSeconds: ThirtyMinutes);

        await using var otherFactory = CreateFactory(["User"], UserId + 1);
        var response = await otherFactory.CreateClient().GetAsync("/api/todo-list-item/estimate-fit?groupBy=list", CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var fit = await response.Content.ReadFromJsonAsync<TodoListItemEstimateFitResponse>(JsonOpts, CancellationToken);

        fit!.Groups.Should().BeEmpty();
        fit.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task EstimateFit_RejectsAMissingOrUnknownGroupBy()
    {
        var client = CreateUserRoleClient();
        (await client.GetAsync("/api/todo-list-item/estimate-fit", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/todo-list-item/estimate-fit?groupBy=activity", CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- helpers

    private Task<(long ItemId, long ActivityId)> SeedCompletedItemAsync(
        string name, int loggedSeconds, int? suggestedSeconds = ThirtyMinutes, DateTime? completedAt = null,
        long? listId = null, long? priorityId = null) =>
        SeedItemAsync(name, isDone: true, loggedSeconds, suggestedSeconds, completedAt, listId, priorityId);

    /// <summary>
    /// One activity per item (<c>todo_list_item</c> is unique on <c>(UserId, ActivityId, TodoListId)</c>). The
    /// completion instant is written straight onto the row: the interceptor's stamp is pinned by the recap tests,
    /// and here the instant is the input.
    /// </summary>
    private async Task<(long ItemId, long ActivityId)> SeedItemAsync(
        string name, bool isDone, int loggedSeconds, int? suggestedSeconds = ThirtyMinutes, DateTime? completedAt = null,
        long? listId = null, long? priorityId = null)
    {
        await using var db = CreateDbContext();
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, name, UserId, CancellationToken);
        var itemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, priorityId ?? _priorityId, listId ?? _listId, UserId, isDone: isDone, ct: CancellationToken,
            suggestedTime: suggestedSeconds is { } s ? new IntTime(s) : null);

        if (isDone)
        {
            var item = await db.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
            item.CompletedTimestamp = completedAt ?? DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync(CancellationToken);
        }

        if (loggedSeconds > 0)
            await LogAsync(activityId, itemId, (completedAt ?? DateTime.UtcNow).AddDays(-2), loggedSeconds);

        return (itemId, activityId);
    }

    private async Task LogAsync(long activityId, long? todoListItemId, DateTime startUtc, int seconds)
    {
        await using var db = CreateDbContext();
        db.Set<ActivityHistory>().Add(new ActivityHistory
        {
            UserId = UserId,
            ActivityId = activityId,
            StartTimestamp = startUtc,
            EndTimestamp = startUtc.AddSeconds(seconds),
            Length = new IntTime(seconds),
            TodoListItemId = todoListItemId
        });
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task SeedBlockAsync(long activityId, long itemId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        await using var db = CreateDbContext();
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, date, UserId, CancellationToken);
        var taskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end, UserId, ct: CancellationToken);
        var task = await db.Set<PlannerTask>().SingleAsync(t => t.Id == taskId, CancellationToken);
        task.TodolistItemId = itemId;
        await db.SaveChangesAsync(CancellationToken);
    }

    private async Task<TodoListItemEstimateFitResponse> FitAsync(string groupBy)
    {
        var response = await CreateUserRoleClient().GetAsync($"/api/todo-list-item/estimate-fit?groupBy={groupBy}", CancellationToken);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return (await response.Content.ReadFromJsonAsync<TodoListItemEstimateFitResponse>(JsonOpts, CancellationToken))!;
    }
}
