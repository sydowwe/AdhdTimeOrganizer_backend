using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// FO2 — time logged anywhere ticks off the plan. History's two create routes now publish
/// <c>ActivityTimeRecordedEvent</c>, which until now only the desktop heartbeat raised, so a pomodoro on the
/// planned activity left its block reading <i>Not started</i>.
/// </summary>
/// <remarks>
/// This is the route half: that a create reaches the handler at all, with the right day, window and item link.
/// The handler's own branch matrix is <c>ActivityTimeAutomationTests</c>, which drives it directly. The fixture
/// user is on UTC unless a test moves it.
/// </remarks>
[Collection("Postgres")]
public class HistoryTicksOffThePlanTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    private static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);

    private static DateTime UtcAt(int hour, int minute = 0) => UtcToday.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    [Fact(DisplayName = "A countdown session as long as the block completes it, with the session's times as actuals")]
    public async Task TimerSession_CompletesThePlannedBlock()
    {
        var activityId = await SeedActivityAsync("timer-completes");
        var taskId = await SeedBlockAsync(activityId, UtcToday, new TimeOnly(10, 0), new TimeOnly(10, 30));

        await PostCreatedAsync("api/activity-history/session", new
        {
            kind = "Timer",
            outcome = "Completed",
            records = new[] { Record(activityId, UtcAt(10), 30, null) }
        });

        var task = await LoadTaskAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.Completed);
        task.ActualStartTime.Should().Be(new TimeOnly(10, 0));
        task.ActualEndTime.Should().Be(new TimeOnly(10, 30), "the session's row ends the work");
    }

    [Fact(DisplayName = "A hand-logged row shorter than the block starts it")]
    public async Task SingleCreate_StartsThePlannedBlock()
    {
        var activityId = await SeedActivityAsync("single-starts");
        var taskId = await SeedBlockAsync(activityId, UtcToday, new TimeOnly(14, 0), new TimeOnly(15, 0));

        await PostCreatedAsync("api/activity-history", new
        {
            activityId,
            startTimestamp = UtcAt(14, 5),
            length = new { hours = 0, minutes = 10 }
        });

        var task = await LoadTaskAsync(taskId);
        task.Status.Should().Be(PlannerTaskStatus.InProgress);
        task.ActualStartTime.Should().Be(new TimeOnly(14, 5));
    }

    /// <summary>
    /// With no block for the activity, the fallback ticks off the item the session was run <i>for</i> — not the
    /// older open item sharing the activity, which the unlinked fallback would pick.
    /// </summary>
    [Fact(DisplayName = "A pomodoro run for a to-do item completes that item, not another on the same activity")]
    public async Task LinkedPomodoro_CompletesItsOwnItem()
    {
        var focusId = await SeedActivityAsync("pomodoro-focus");
        var restId = await SeedActivityAsync("pomodoro-rest");
        long olderId, linkedId;
        await using (var db = CreateDbContext())
        {
            var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
            var olderListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ct: CancellationToken);
            var linkedListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ct: CancellationToken);
            olderId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, focusId, priorityId, olderListId,
                ct: CancellationToken, suggestedTime: new IntTime(0, 25));
            linkedId = await TodoListTestSeedHelper.SeedTodoListItemAsync(db, focusId, priorityId, linkedListId,
                ct: CancellationToken, suggestedTime: new IntTime(0, 25));
        }

        await PostCreatedAsync("api/activity-history/session", new
        {
            kind = "Pomodoro",
            outcome = "Completed",
            plannedFocusPeriods = 1,
            completedFocusPeriods = 1,
            todoListItemId = linkedId,
            records = new[]
            {
                Record(focusId, UtcAt(9), 25, "Focus"),
                Record(restId, UtcAt(9, 25), 5, "ShortBreak")
            }
        });

        await using var read = CreateDbContext();
        (await read.Set<TodoListItem>().SingleAsync(i => i.Id == linkedId, CancellationToken)).IsDone.Should().BeTrue();
        (await read.Set<TodoListItem>().SingleAsync(i => i.Id == olderId, CancellationToken)).IsDone.Should().BeFalse(
            "the session named its item; the older one on the same activity is not the one that was worked on");
    }

    /// <summary>
    /// The day is the user's, not UTC's. The row is placed half an hour from local midnight on the side where the
    /// two calendars disagree, so a publisher reading the UTC date would look for the block on the wrong day.
    /// </summary>
    [Fact(DisplayName = "The block is found on the user's local day, not the UTC date of the row")]
    public async Task LocalDay_IsTheUsersDay()
    {
        TimeZoneInfo zone;
        await using (var db = CreateDbContext())
            zone = await PlanningTestSeedHelper.UseZoneOffsetFromUtcTodayAsync(db, ct: CancellationToken);

        var localToday = PlanningTestSeedHelper.TodayIn(zone);
        var aheadOfUtc = localToday > UtcToday;
        var localStart = aheadOfUtc ? new TimeOnly(0, 30) : new TimeOnly(23, 0);
        var startUtc = WallClockZone.ToUtc(localToday, localStart, zone);
        DateOnly.FromDateTime(startUtc).Should().NotBe(localToday, "the case only proves anything when the two dates differ");

        var activityId = await SeedActivityAsync("local-day");
        var taskId = await SeedBlockAsync(activityId, localToday, localStart, localStart.AddMinutes(30));

        await PostCreatedAsync("api/activity-history/session", new
        {
            kind = "Timer",
            outcome = "Completed",
            records = new[] { Record(activityId, startUtc, 30, null) }
        });

        (await LoadTaskAsync(taskId)).Status.Should().Be(PlannerTaskStatus.Completed);
    }

    // ---- a sitting logged as doing the item (CountsAsRepetition) ----------------------------------

    /// <summary>
    /// The bug FO2 left open: the 25-minute sitting reached the item's suggested time, and the fallback set
    /// <c>DoneCount = TotalCount</c>, so one pomodoro finished a four-repetition item.
    /// </summary>
    [Fact(DisplayName = "FO2: a pomodoro logged as one sitting of a 4× item is one repetition, and the fourth finishes it")]
    public async Task Repetition_StepsACountedItemOncePerSitting()
    {
        var focusId = await SeedActivityAsync("rep-focus");
        var restId = await SeedActivityAsync("rep-rest");
        var itemId = await SeedTodoItemAsync(focusId, totalCount: 4, suggestedTime: new IntTime(0, 25));

        for (var sitting = 0; sitting < 4; sitting++)
        {
            await PostCreatedAsync("api/activity-history/session", new
            {
                kind = "Pomodoro",
                outcome = "Completed",
                plannedFocusPeriods = 1,
                completedFocusPeriods = 1,
                todoListItemId = itemId,
                countsAsRepetition = true,
                records = new[]
                {
                    Record(focusId, UtcAt(8 + sitting), 25, "Focus"),
                    Record(restId, UtcAt(8 + sitting, 25), 5, "ShortBreak")
                }
            });

            var item = await LoadTodoItemAsync(itemId);
            item.DoneCount.Should().Be(sitting + 1, "each sitting is one repetition, however much time the day holds");
            item.IsDone.Should().Be(sitting == 3);
        }
    }

    [Fact(DisplayName = "FO2: a sitting logged as doing a plain item finishes it, however short")]
    public async Task Repetition_FinishesAPlainItem()
    {
        var activityId = await SeedActivityAsync("rep-plain");
        var itemId = await SeedTodoItemAsync(activityId, suggestedTime: new IntTime(1, 0));

        await PostCreatedAsync("api/activity-history", new
        {
            activityId,
            startTimestamp = UtcAt(11),
            length = new { hours = 0, minutes = 5 },
            todoListItemId = itemId,
            countsAsRepetition = true
        });

        (await LoadTodoItemAsync(itemId)).IsDone.Should().BeTrue("logging time from the item says it happened");
    }

    /// <summary>
    /// The publisher cuts a row at the user's midnight and raises one event per day. Only the first may count, or
    /// a late session would be two repetitions.
    /// </summary>
    [Fact(DisplayName = "FO2: a sitting that crosses midnight is still one repetition")]
    public async Task Repetition_AcrossMidnight_CountsOnce()
    {
        var activityId = await SeedActivityAsync("rep-midnight");
        var itemId = await SeedTodoItemAsync(activityId, totalCount: 4);

        await PostCreatedAsync("api/activity-history", new
        {
            activityId,
            startTimestamp = UtcToday.ToDateTime(new TimeOnly(23, 40), DateTimeKind.Utc).AddDays(-1),
            length = new { hours = 0, minutes = 40 },
            todoListItemId = itemId,
            countsAsRepetition = true
        });

        (await LoadTodoItemAsync(itemId)).DoneCount.Should().Be(1);
    }

    /// <summary>
    /// Home's planner-block timer links the row to the block's to-do only so the recap credits it; the block
    /// decides the item. Time alone never finishes a counted item, however far past its suggested time.
    /// </summary>
    [Fact(DisplayName = "FO2: time linked to a counted item only for the recap ticks nothing")]
    public async Task RecapOnlyLink_LeavesACountedItemAlone()
    {
        var activityId = await SeedActivityAsync("rep-recap-only");
        var itemId = await SeedTodoItemAsync(activityId, totalCount: 4, suggestedTime: new IntTime(0, 25));

        await PostCreatedAsync("api/activity-history", new
        {
            activityId,
            startTimestamp = UtcAt(12),
            length = new { hours = 1, minutes = 0 },
            todoListItemId = itemId
        });

        var item = await LoadTodoItemAsync(itemId);
        item.DoneCount.Should().Be(0);
        item.IsDone.Should().BeFalse();
    }

    [Fact(DisplayName = "FO2: a sitting logged as doing a counted routine item is one repetition")]
    public async Task Repetition_StepsACountedRoutineItem()
    {
        var activityId = await SeedActivityAsync("rep-routine");
        long itemId;
        await using (var db = CreateDbContext())
        {
            var periodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, ct: CancellationToken);
            itemId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, periodId, totalCount: 3, doneCount: 0,
                ct: CancellationToken);
        }

        await PostCreatedAsync("api/activity-history", new
        {
            activityId,
            startTimestamp = UtcAt(13),
            length = new { hours = 0, minutes = 20 },
            routineTodoListId = itemId,
            countsAsRepetition = true
        });

        await using var read = CreateDbContext();
        var routineItem = await read.Set<RoutineTodoList>()
            .SingleAsync(r => r.Id == itemId, CancellationToken);
        routineItem.DoneCount.Should().Be(1);
        routineItem.IsDone.Should().BeFalse();
    }

    [Fact(DisplayName = "FO2: countsAsRepetition without an item link is a 400 on both creates")]
    public async Task Repetition_WithoutALink_IsRejected()
    {
        var activityId = await SeedActivityAsync("rep-no-link");
        var client = CreateUserRoleClient();

        var single = await client.PostAsJsonAsync("api/activity-history", new
        {
            activityId,
            startTimestamp = UtcAt(15),
            length = new { hours = 0, minutes = 10 },
            countsAsRepetition = true
        }, JsonOpts, CancellationToken);
        single.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var session = await client.PostAsJsonAsync("api/activity-history/session", new
        {
            kind = "Timer",
            outcome = "Completed",
            countsAsRepetition = true,
            records = new[] { Record(activityId, UtcAt(16), 10, null) }
        }, JsonOpts, CancellationToken);
        session.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private async Task<long> SeedTodoItemAsync(long activityId, int? totalCount = null, IntTime? suggestedTime = null)
    {
        await using var db = CreateDbContext();
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, ct: CancellationToken);
        var listId = await TodoListTestSeedHelper.SeedTodoListAsync(db, ct: CancellationToken);
        return await TodoListTestSeedHelper.SeedTodoListItemAsync(db, activityId, priorityId, listId,
            totalCount: totalCount, doneCount: totalCount is null ? null : 0, ct: CancellationToken, suggestedTime: suggestedTime);
    }

    private async Task<TodoListItem> LoadTodoItemAsync(long itemId)
    {
        await using var db = CreateDbContext();
        return await db.Set<TodoListItem>().SingleAsync(i => i.Id == itemId, CancellationToken);
    }

    private static object Record(long activityId, DateTime start, int minutes, string? phase) => new
    {
        activityId,
        startTimestamp = start,
        length = new { hours = 0, minutes },
        phase
    };

    private async Task PostCreatedAsync(string route, object body)
    {
        var response = await CreateUserRoleClient().PostAsJsonAsync(route, body, JsonOpts, CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();
        return await PlanningTestSeedHelper.SeedActivityAsync(db, $"{name}-{Guid.NewGuid():N}", ct: CancellationToken);
    }

    private async Task<long> SeedBlockAsync(long activityId, DateOnly day, TimeOnly start, TimeOnly end)
    {
        await using var db = CreateDbContext();
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, day, UserId, CancellationToken);
        return await PlanningTestSeedHelper.SeedPlannerTaskAsync(db, activityId, calendarId, start, end, ct: CancellationToken);
    }

    private async Task<PlannerTask> LoadTaskAsync(long taskId)
    {
        await using var db = CreateDbContext();
        return await db.Set<PlannerTask>().IgnoreQueryFilters().SingleAsync(t => t.Id == taskId, CancellationToken);
    }
}
