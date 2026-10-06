using System.Net;
using System.Net.Http.Json;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
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

    // ---- helpers ---------------------------------------------------------------------------------

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
