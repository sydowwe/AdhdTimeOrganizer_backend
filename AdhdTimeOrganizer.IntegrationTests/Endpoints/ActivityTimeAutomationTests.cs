using AdhdTimeOrganizer.application.eventHandler;
using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.IntegrationTests.Infrastructure;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Framework.Testing;
using Xunit;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// Pins the completion automation that the Tracking extraction moved out of
/// <c>DesktopActivityHeartbeatEndpoint</c> and into <see cref="ActivityTimeRecordedEventHandler"/>.
/// <para>
/// This is the one place in the slice programme where a <b>write</b> crossed a slice boundary, so it
/// could not be inverted the way History's read filters were — the heartbeat now announces day totals
/// through <see cref="ActivityTimeRecordedEvent"/> and the host decides what to complete. That handler
/// deliberately swallows its own exceptions (the ingest already committed; a 500 would only make the
/// agent re-send the window), so <b>a break in it is silent</b>. Nothing but assertions on the rows
/// will catch it, which is what this file is.
/// </para>
/// <para>
/// The branch matrix is exercised against the handler directly rather than over HTTP. The route half —
/// that the heartbeat reaches this at all — is
/// <c>ExtensionActivityTrackingTests.Heartbeat_MappedEntry_AttributesTimeAndCompletesPlannerTask</c>;
/// the two together cover publish, discovery and logic. Driving all five branches through extension
/// logins would buy nothing extra and cost five more auth round-trips.
/// </para>
/// </summary>
[Collection("Postgres")]
public class ActivityTimeAutomationTests(AppDbContextFixture fixture) : PostgresTestBase(fixture)
{
    private const long UserId = FakeLoggedUserService.TestUserId;

    /// <summary>
    /// Tracked time at or past the planner task's planned duration completes it. This is the rule the
    /// heartbeat used to apply inline.
    /// </summary>
    [Fact]
    public async Task PlannerTask_WithEnoughTrackedTime_IsCompleted()
    {
        var activityId = await SeedActivityAsync("planner-complete");
        var taskId = await SeedPlannerTaskAsync(activityId, new TimeOnly(9, 0), new TimeOnly(9, 30));

        await RunAsync(activityId, totalSecondsOnDay: 1800);

        await using var db = CreateDbContext();
        var task = await db.Set<PlannerTask>().FirstAsync(t => t.Id == taskId, CancellationToken);
        task.Status.Should().Be(PlannerTaskStatus.Completed);
    }

    /// <summary>
    /// Short of the duration it moves to InProgress, not Completed — the same call, different branch.
    /// A handler that completed unconditionally would still pass the test above.
    /// </summary>
    [Fact]
    public async Task PlannerTask_WithPartialTrackedTime_IsInProgress()
    {
        var activityId = await SeedActivityAsync("planner-partial");
        var taskId = await SeedPlannerTaskAsync(activityId, new TimeOnly(10, 0), new TimeOnly(11, 0));

        await RunAsync(activityId, totalSecondsOnDay: 60);

        await using var db = CreateDbContext();
        var task = await db.Set<PlannerTask>().FirstAsync(t => t.Id == taskId, CancellationToken);
        task.Status.Should().Be(PlannerTaskStatus.InProgress);
    }

    /// <summary>
    /// With no planner task for the day, the fallback ticks off a to-do item whose SuggestedTime the
    /// tracked seconds have reached.
    /// </summary>
    [Fact]
    public async Task NoPlannerTask_TodoItemPastSuggestedTime_IsDone()
    {
        var activityId = await SeedActivityAsync("todo-fallback");
        var itemId = await SeedTodoItemAsync(activityId, suggestedSeconds: 600);

        await RunAsync(activityId, totalSecondsOnDay: 900);

        await using var db = CreateDbContext();
        var item = await db.Set<TodoListItem>().FirstAsync(i => i.Id == itemId, CancellationToken);
        item.IsDone.Should().BeTrue();
    }

    /// <summary>
    /// Same fallback, routine side — and it must also bump the streak, which is
    /// <c>RoutineResetService.UpdateItemStreak</c>'s job and was easy to drop in the move.
    /// </summary>
    [Fact]
    public async Task NoPlannerTask_RoutineItemPastSuggestedTime_IsDoneAndStreakBumped()
    {
        var activityId = await SeedActivityAsync("routine-fallback");
        var itemId = await SeedRoutineItemAsync(activityId, suggestedSeconds: 300);

        await RunAsync(activityId, totalSecondsOnDay: 300);

        await using var db = CreateDbContext();
        var item = await db.Set<RoutineTodoList>().FirstAsync(r => r.Id == itemId, CancellationToken);
        item.IsDone.Should().BeTrue();
        item.Streak.Should().BeGreaterThan(0, "UpdateItemStreak must still run on the fallback path");
    }

    /// <summary>
    /// The load-bearing exclusivity rule, and the reason this automation is <b>one host-side handler
    /// rather than one handler per owning slice</b>: the to-do fallback runs only when no planner task
    /// matched. Split across independent <c>Mode.WaitForAll</c> subscribers there is no ordering and no
    /// veto, so a Planning handler and a TodoLists handler would both fire here and the activity would
    /// be double-completed — silently, since neither would know about the other.
    /// </summary>
    [Fact]
    public async Task PlannerTaskMatched_TodoFallback_DoesNotRun()
    {
        var activityId = await SeedActivityAsync("exclusive");
        await SeedPlannerTaskAsync(activityId, new TimeOnly(8, 0), new TimeOnly(8, 15));
        var itemId = await SeedTodoItemAsync(activityId, suggestedSeconds: 60);

        // Past both thresholds, so only the exclusivity rule can keep the to-do item open.
        await RunAsync(activityId, totalSecondsOnDay: 3600);

        await using var db = CreateDbContext();
        var item = await db.Set<TodoListItem>().FirstAsync(i => i.Id == itemId, CancellationToken);
        item.IsDone.Should().BeFalse("a matched planner task must suppress the to-do/routine fallback");
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>
    /// Drives the handler the way the event bus would. Built by hand rather than resolved, because
    /// FastEndpoints keeps its handler registry outside DI — the wiring half is covered over HTTP in
    /// <c>ExtensionActivityTrackingTests</c>.
    /// </summary>
    private async Task RunAsync(long activityId, int totalSecondsOnDay)
    {
        var scopeFactory = Fixture.UnauthenticatedFactory.Services.GetRequiredService<IServiceScopeFactory>();
        var handler = new ActivityTimeRecordedEventHandler(
            scopeFactory, NullLogger<ActivityTimeRecordedEventHandler>.Instance);

        await handler.HandleAsync(
            new ActivityTimeRecordedEvent(UserId, Today, [new ActivityDayTotal(activityId, totalSecondsOnDay)]),
            CancellationToken);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<long> SeedActivityAsync(string name)
    {
        await using var db = CreateDbContext();

        var role = new ActivityRole { UserId = UserId, Name = $"{name}-role", Color = "#123456" };
        db.Set<ActivityRole>().Add(role);
        await db.SaveChangesAsync(CancellationToken);

        var activity = new Activity { UserId = UserId, Name = name, RoleId = role.Id };
        db.Set<Activity>().Add(activity);
        await db.SaveChangesAsync(CancellationToken);
        return activity.Id;
    }

    private async Task<long> SeedPlannerTaskAsync(long activityId, TimeOnly start, TimeOnly end)
    {
        await using var db = CreateDbContext();

        // The handler matches on Calendar.Date, so the day row has to exist and be the same day the
        // event names. One calendar per test day per user, reused across activities.
        var calendar = await db.Set<Calendar>().FirstOrDefaultAsync(c => c.UserId == UserId && c.Date == Today, CancellationToken);
        if (calendar == null)
        {
            calendar = new Calendar
            {
                UserId = UserId,
                Date = Today,
                DayType = DayType.Workday,
                WakeUpTime = new TimeOnly(7, 0),
                BedTime = new TimeOnly(23, 0)
            };
            db.Set<Calendar>().Add(calendar);
            await db.SaveChangesAsync(CancellationToken);
        }

        var task = new PlannerTask
        {
            UserId = UserId,
            ActivityId = activityId,
            CalendarId = calendar.Id,
            StartTime = start,
            EndTime = end,
            IsBackground = false,
            Status = PlannerTaskStatus.NotStarted
        };
        db.Set<PlannerTask>().Add(task);
        await db.SaveChangesAsync(CancellationToken);
        return task.Id;
    }

    private async Task<long> SeedTodoItemAsync(long activityId, int suggestedSeconds)
    {
        await using var db = CreateDbContext();

        var priority = new TaskPriority { UserId = UserId, Text = $"P{activityId}", Color = "#654321", Priority = (int)activityId };
        db.Set<TaskPriority>().Add(priority);
        await db.SaveChangesAsync(CancellationToken);

        var item = new TodoListItem
        {
            UserId = UserId,
            ActivityId = activityId,
            TaskPriorityId = priority.Id,
            SuggestedTime = new IntTime(suggestedSeconds)
        };
        db.Set<TodoListItem>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item.Id;
    }

    private async Task<long> SeedRoutineItemAsync(long activityId, int suggestedSeconds)
    {
        await using var db = CreateDbContext();

        var period = new RoutineTimePeriod
        {
            UserId = UserId,
            Text = $"Period {activityId}",
            Color = "#abcdef",
            LengthInDays = 1,
            ResetAnchorDay = 1,
            StreakThreshold = 1,
            StreakGraceDays = 0
        };
        db.Set<RoutineTimePeriod>().Add(period);
        await db.SaveChangesAsync(CancellationToken);

        var item = new RoutineTodoList
        {
            UserId = UserId,
            ActivityId = activityId,
            TimePeriodId = period.Id,
            SuggestedTime = new IntTime(suggestedSeconds)
        };
        db.Set<RoutineTodoList>().Add(item);
        await db.SaveChangesAsync(CancellationToken);
        return item.Id;
    }
}
