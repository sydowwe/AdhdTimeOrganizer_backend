using System.Reflection;
using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.domain.model.entity.timer;
using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using AdhdTimeOrganizer.History.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking;
using AdhdTimeOrganizer.Tracking.domain.model.entity.activityTracking.desktop;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.domain.valueObject;
using Sydowwe.Notifications.domain.entity;

namespace AdhdTimeOrganizer.IntegrationTests.Endpoints;

/// <summary>
/// What "all of a user's data" means to the erasure tests: one row in every table the export claims to walk, plus the
/// ones the erasure has to reach without a cascade helping it. Shared by the export-completeness test, the
/// self-deletion test and the unconfirmed-account purge test, so the three can never disagree about it.
/// </summary>
internal static class UserDataInventory
{
    public static async Task<SeededUserData> SeedEverySliceAsync(DbContext db, long userId, CancellationToken ct)
    {
        var activityId = await PlanningTestSeedHelper.SeedActivityAsync(db, $"Slice Activity {Guid.NewGuid():N}", userId, ct);
        var calendarId = await PlanningTestSeedHelper.SeedCalendarAsync(db, DateOnly.FromDateTime(DateTime.UtcNow), userId, ct);
        var plannerTaskId = await PlanningTestSeedHelper.SeedPlannerTaskAsync(
            db, activityId, calendarId, new TimeOnly(9, 0), new TimeOnly(10, 0), userId, ct: ct);
        var templateId = await PlanningTestSeedHelper.SeedTaskPlannerDayTemplateAsync(db, userId, ct: ct);

        // A stretch of time away holding the seeded day, so both the row and the day's link to it are in the set.
        var timeAway = new TimeAway
        {
            UserId = userId,
            Name = "Slice trip",
            Kind = DayType.Vacation,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow),
            TemplateId = templateId
        };
        db.Set<TimeAway>().Add(timeAway);
        await db.SaveChangesAsync(ct);
        (await db.Set<Calendar>().IgnoreQueryFilters().SingleAsync(c => c.Id == calendarId, ct)).TimeAwayId = timeAway.Id;

        var todoListId = await TodoListTestSeedHelper.SeedTodoListAsync(db, userId, $"Slice List {Guid.NewGuid():N}", ct: ct);
        var priorityId = await TodoListTestSeedHelper.SeedTaskPriorityAsync(db, 1, userId, ct: ct);
        var todoListItemId = await TodoListTestSeedHelper.SeedTodoListItemAsync(
            db, activityId, priorityId, todoListId, userId, ct: ct);

        var timePeriodId = await TodoListTestSeedHelper.SeedRoutineTimePeriodAsync(db, userId, ct: ct);
        var routineTodoListId = await TodoListTestSeedHelper.SeedRoutineTodoListAsync(db, activityId, timePeriodId, userId, ct: ct);

        // Inside a timer session, so the session table is exported and erased along with its row.
        var historySession = new ActivityHistorySession
        {
            UserId = userId,
            Kind = TimerSessionKind.Timer,
            Outcome = TimerSessionOutcome.Completed
        };
        var history = new ActivityHistory
        {
            UserId = userId,
            ActivityId = activityId,
            StartTimestamp = DateTime.UtcNow.AddHours(-1),
            EndTimestamp = DateTime.UtcNow.AddMinutes(-30),
            Length = new IntTime(0, 30),
            Session = historySession
        };
        db.Set<ActivityHistory>().Add(history);

        // RecordDate is derived from WindowStart; "now" keeps both entries inside
        // AppDbContext.CurrentPartitionDate, which the web-extension query filter enforces on reads.
        var windowStart = DateTime.UtcNow;
        var webEntry = new WebExtensionActivityEntry
        {
            UserId = userId,
            WindowStart = windowStart,
            Domain = "slice.example",
            Url = null,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsFinal = true
        };
        db.Set<WebExtensionActivityEntry>().Add(webEntry);

        var desktopEntry = new DesktopActivityEntry
        {
            UserId = userId,
            WindowStart = windowStart,
            ProcessName = "slice.exe",
            ProductName = "Slice",
            WindowTitle = "t",
            ExecutablePath = "/usr/bin/slice",
            IsFullscreen = false,
            ActiveSeconds = 60,
            BackgroundSeconds = 0,
            IsPlayingSound = false,
            ActiveMonitor = 0
        };
        db.Set<DesktopActivityEntry>().Add(desktopEntry);

        // Quiet hours carries no FK to the user table on purpose (see NotificationQuietHours' XML doc), so
        // only NotificationSubjectDataEraser can ever remove it.
        var quietHours = new NotificationQuietHours { UserId = userId, StartMinute = 22 * 60, EndMinute = 6 * 60 };
        db.Set<NotificationQuietHours>().Add(quietHours);

        var timerPreset = new TimerPreset { UserId = userId, Duration = 25 };
        db.Set<TimerPreset>().Add(timerPreset);

        await db.SaveChangesAsync(ct);

        return new SeededUserData(
            activityId, calendarId, plannerTaskId, templateId, todoListId, todoListItemId,
            routineTodoListId, history.Id, historySession.Id, timerPreset.Id, quietHours.Id, timeAway.Id);
    }

    /// <summary>
    /// Nothing of <paramref name="userId"/>'s is left in any table <see cref="SeedEverySliceAsync"/> fills, nor in any
    /// user-scoped table at all (<see cref="AssertNoUserScopedRowsLeftAsync"/>). Quiet hours is in here on purpose: it
    /// carries no FK to the user table, so only <c>NotificationSubjectDataEraser</c> can remove it and no cascade covers
    /// for a broken fan-out.
    /// </summary>
    public static async Task AssertErasedAsync(DbContext db, long userId, CancellationToken ct)
    {
        await AssertNoUserScopedRowsLeftAsync(db, userId, ct);

        await AssertNoRowsLeftAsync<Activity>(db, userId, ct);
        await AssertNoRowsLeftAsync<ActivityRole>(db, userId, ct);
        await AssertNoRowsLeftAsync<Calendar>(db, userId, ct);
        await AssertNoRowsLeftAsync<PlannerTask>(db, userId, ct);
        await AssertNoRowsLeftAsync<TaskPlannerDayTemplate>(db, userId, ct);
        await AssertNoRowsLeftAsync<TimeAway>(db, userId, ct);
        await AssertNoRowsLeftAsync<TodoList>(db, userId, ct);
        await AssertNoRowsLeftAsync<TodoListItem>(db, userId, ct);
        await AssertNoRowsLeftAsync<TaskPriority>(db, userId, ct);
        await AssertNoRowsLeftAsync<RoutineTodoList>(db, userId, ct);
        await AssertNoRowsLeftAsync<RoutineTimePeriod>(db, userId, ct);
        await AssertNoRowsLeftAsync<ActivityHistory>(db, userId, ct);
        await AssertNoRowsLeftAsync<ActivityHistorySession>(db, userId, ct);
        await AssertNoRowsLeftAsync<WebExtensionActivityEntry>(db, userId, ct);
        await AssertNoRowsLeftAsync<DesktopActivityEntry>(db, userId, ct);
        await AssertNoRowsLeftAsync<TimerPreset>(db, userId, ct);

        (await db.Set<NotificationQuietHours>().AnyAsync(q => q.UserId == userId, ct))
            .Should().BeFalse("quiet hours has no FK to the user -- only the NotificationSubjectDataEraser fan-out removes it");
    }

    /// <summary>
    /// Every <see cref="IEntityWithUser"/> in the model, not a hand-kept list: what the per-user default seeders create at
    /// registration changes without anyone touching a test, and a new user-scoped entity is covered the day it is mapped.
    /// Query filters are lifted, so a row the user could no longer see still counts.
    /// </summary>
    public static async Task AssertNoUserScopedRowsLeftAsync(DbContext db, long userId, CancellationToken ct) =>
        (await UserScopedRowsAsync(db, userId, ct)).Should().BeEmpty("no user-scoped row may outlive a deleted account");

    /// <summary>Per user-scoped entity holding rows of <paramref name="userId"/>'s, <c>"Name xCount"</c>.</summary>
    public static async Task<IReadOnlyList<string>> UserScopedRowsAsync(DbContext db, long userId, CancellationToken ct)
    {
        var userScoped = db.Model.GetEntityTypes()
            .Where(t => typeof(IEntityWithUser).IsAssignableFrom(t.ClrType) && t.FindPrimaryKey() is not null)
            .Select(t => t.ClrType)
            .Distinct();

        var leftovers = new List<string>();
        foreach (var type in userScoped)
        {
            var count = await (Task<int>)CountRowsMethod.MakeGenericMethod(type).Invoke(null, [db, userId, ct])!;
            if (count > 0)
                leftovers.Add($"{type.Name} x{count}");
        }

        return leftovers;
    }

    private static readonly MethodInfo CountRowsMethod =
        typeof(UserDataInventory).GetMethod(nameof(CountRowsAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static Task<int> CountRowsAsync<TEntity>(DbContext db, long userId, CancellationToken ct)
        where TEntity : class, IEntityWithUser =>
        db.Set<TEntity>().IgnoreQueryFilters().CountAsync(e => e.UserId == userId, ct);

    private static async Task AssertNoRowsLeftAsync<TEntity>(DbContext db, long userId, CancellationToken ct)
        where TEntity : class, IEntityWithUser =>
        (await db.Set<TEntity>().IgnoreQueryFilters().AnyAsync(e => e.UserId == userId, ct))
            .Should().BeFalse($"{typeof(TEntity).Name} rows for a deleted account must not survive the erasure");
}

internal sealed record SeededUserData(
    long ActivityId,
    long CalendarId,
    long PlannerTaskId,
    long TemplateId,
    long TodoListId,
    long TodoListItemId,
    long RoutineTodoListId,
    long ActivityHistoryId,
    long ActivityHistorySessionId,
    long TimerPresetId,
    long QuietHoursId,
    long TimeAwayId);
