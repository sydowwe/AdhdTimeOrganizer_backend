using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Sydowwe.Framework.domain.helper;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.eventHandler;

/// <summary>
/// Drives planner-task / to-do / routine completion from tracked activity time. This logic ran inline
/// inside <c>DesktopActivityHeartbeatEndpoint</c> until the Tracking slice was extracted; it is
/// unchanged apart from reading its input from the event instead of from the tracking tables.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>One handler, host-side, on purpose — do not split it into one handler per owning slice.</b>
/// The rule is exclusive: the to-do / routine fallback runs <em>only</em> when no planner task
/// matched. FastEndpoints' <c>Mode.WaitForAll</c> gives independent subscribers no ordering and no
/// veto, so a Planning handler and a TodoLists handler could not express "unless the other one fired"
/// — an activity that has both a planner task and a to-do item would be double-completed, silently.
/// The host already references every slice and already hosts the three completion fan-out handlers,
/// so it is the natural owner. See <see cref="ActivityTimeRecordedEvent"/>.
/// </para>
/// <para>
/// Failures are logged, not rethrown. The tracked entries and their attribution committed before this
/// handler runs, and the ingest client cannot do anything useful with a 500 for work that already
/// succeeded — it would simply re-send the same window. The cost is that a break here is silent, which
/// is what <c>ActivityTimeAutomationTests</c> exists to catch.
/// </para>
/// </remarks>
public class ActivityTimeRecordedEventHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<ActivityTimeRecordedEventHandler> logger) : IEventHandler<ActivityTimeRecordedEvent>
{
    public async Task HandleAsync(ActivityTimeRecordedEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var services = new Collaborators(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IReminderRegistrationService>(),
            scope.ServiceProvider.GetRequiredService<IActivityTimeAttributionSink>(),
            scope.ServiceProvider.GetRequiredService<IUserTimeZoneResolver>());

        foreach (var total in eventModel.Totals)
        {
            try
            {
                await AutomateActivityAsync(services, eventModel, total, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per activity, so one bad row does not cost the rest of the batch its automation.
                logger.LogError(ex, "Failed to automate completion for activity {ActivityId} of user {UserId}",
                    total.ActivityId, eventModel.UserId);
            }
        }
    }

    /// <remarks>
    /// Every write here follows the rules a user-driven status change follows
    /// (<c>PatchPlannerTaskStatusEndpoint</c>), because the task cannot tell the two apart afterwards:
    /// <list type="bullet">
    /// <item>the status goes through <see cref="PlannerTask.ApplyStatus"/>;</item>
    /// <item>the actual times are recorded on the user's clock, or a tracker-completed task is
    /// "unmeasurable" in the plan-vs-actual trend — counted as planned, with no actual minutes;</item>
    /// <item>the task's reminder is re-synced, or a completed task keeps nudging
    /// (<c>Reminder.RemindAt</c> gotcha in Planning's <c>docs/summary.md</c>).</item>
    /// </list>
    /// Time tracked <em>anywhere</em> in the day counts toward a block, not only time inside it: work done at
    /// 10:00 for a block planned at 14:00 still got done, and a rule that says otherwise teaches an ADHD user
    /// that the plan is a trap. What it may not do is count twice — see <see cref="UnclaimedSecondsAsync"/>.
    /// </remarks>
    private static async Task AutomateActivityAsync(
        Collaborators services,
        ActivityTimeRecordedEvent eventModel,
        ActivityDayTotal total,
        CancellationToken ct)
    {
        var dbContext = services.Db;
        var userId = eventModel.UserId;

        // Every open candidate, ordered, so the pick below is deterministic. A bare FirstOrDefault with no
        // OrderBy moved whichever of two same-activity blocks Postgres happened to return first.
        var openTasks = await dbContext.Set<PlannerTask>()
            .Where(pt => pt.UserId == userId && pt.ActivityId == total.ActivityId
                                             && pt.Calendar.Date == eventModel.Day
                                             && pt.Status != PlannerTaskStatus.Completed
                                             && pt.Status != PlannerTaskStatus.Cancelled)
            .OrderBy(pt => pt.StartTime)
            .ThenBy(pt => pt.Id)
            .ToListAsync(ct);

        if (openTasks.Count == 0)
        {
            await AutomateWithoutPlannerTaskAsync(dbContext, userId, total, ct);
            return;
        }

        // The block the user is in right now, if any; otherwise the earliest one still open. IsBetween is
        // half-open and midnight-wrap aware, like the task spans it is reading.
        var plannerTask = openTasks.FirstOrDefault(pt => eventModel.WindowStart.IsBetween(pt.StartTime, pt.EndTime))
                          ?? openTasks[0];

        var durationSeconds = (int)(plannerTask.EndTime - plannerTask.StartTime).TotalSeconds;
        var availableSeconds = await UnclaimedSecondsAsync(services, eventModel, total, ct);

        // Every second of the day is already claimed by a completed block, so nothing was observed for this one.
        // A heartbeat always brings its own fresh minute, but History's paths do not: a timer run from the morning
        // "Coding" block is completed by the planner (claiming up to now), and the history row it then logs is
        // inside that claim — it must not flip the afternoon "Coding" block to InProgress (FO2).
        if (availableSeconds <= 0)
            return;

        var wasCompleted = durationSeconds > 0 && availableSeconds >= durationSeconds;
        var newStatus = wasCompleted ? PlannerTaskStatus.Completed : PlannerTaskStatus.InProgress;

        // An InProgress task that is still short of its threshold has nothing to record.
        if (plannerTask.Status == newStatus)
            return;

        plannerTask.ApplyStatus(newStatus);
        // The first window that moved the task is when work on it began. A task completed straight from
        // NotStarted had its time tracked before anything observed it — created after the fact, say — so
        // its span is the planned length ending now, rather than a one-minute run that never happened.
        plannerTask.ActualStartTime ??= wasCompleted
            ? eventModel.WindowEnd.Add(-TimeSpan.FromSeconds(durationSeconds))
            : eventModel.WindowStart;
        if (wasCompleted)
            plannerTask.ActualEndTime = eventModel.WindowEnd;

        await dbContext.SaveChangesAsync(ct);

        await services.Reminders.SyncForPlannerTasksAsync([plannerTask.Id], ct);

        if (wasCompleted)
            await new PlannerTaskIsDoneChangedEvent(total.ActivityId, userId, true, plannerTask.TodolistItemId)
                .PublishAsync(Mode.WaitForAll, ct);
    }

    /// <summary>
    /// The tracked seconds no completed block has already used. Two "Coding" blocks on one day used to be
    /// measured against the same whole-day total, so the moment the first completed, the next heartbeat
    /// completed the second with no further work.
    /// <para>
    /// A completed same-activity block <b>claims everything tracked up to its end</b>; only time after the
    /// latest such end is available to the next block. "Up to its end" rather than "its planned length"
    /// because a block ticked by hand may have had none of its time tracked at all, and subtracting a length
    /// it never consumed would leave the next block unable to complete. The end is the actual end when one was
    /// recorded, else the planned end — capped at this window, since a block ticked early cannot claim time
    /// that has not happened yet.
    /// </para>
    /// <para>
    /// With nothing completed yet the whole-day total the event carries is the answer, and no query is made.
    /// </para>
    /// </summary>
    private static async Task<int> UnclaimedSecondsAsync(
        Collaborators services, ActivityTimeRecordedEvent eventModel, ActivityDayTotal total, CancellationToken ct)
    {
        var completed = await services.Db.Set<PlannerTask>()
            .Where(pt => pt.UserId == eventModel.UserId && pt.ActivityId == total.ActivityId
                                                        && pt.Calendar.Date == eventModel.Day
                                                        && pt.Status == PlannerTaskStatus.Completed)
            .Select(pt => new { pt.StartTime, pt.EndTime, pt.ActualStartTime, pt.ActualEndTime })
            .ToListAsync(ct);

        if (completed.Count == 0)
            return total.TotalSecondsOnDay;

        var windowEnd = OffsetInDay(eventModel.WindowStart, eventModel.WindowEnd);
        var claimedUntil = completed
            .Select(pt => OffsetInDay(pt.ActualStartTime ?? pt.StartTime, pt.ActualEndTime ?? pt.EndTime))
            .Max();
        if (claimedUntil > windowEnd)
            claimedUntil = windowEnd;

        var timeZone = await services.TimeZones.GetAsync(eventModel.UserId, ct);
        var fromUtc = WallClockZone.ToUtc(eventModel.Day.ToDateTime(TimeOnly.MinValue).Add(claimedUntil), timeZone);
        var toUtc = WallClockZone.ToUtc(eventModel.Day.AddDays(1), TimeOnly.MinValue, timeZone);

        return await services.Sink.TotalSecondsBetweenAsync(services.Db, eventModel.UserId, total.ActivityId, fromUtc, toUtc, ct);
    }

    /// <summary>
    /// How far into the day <paramref name="end"/> is, for a span starting at <paramref name="start"/>. An end
    /// before its start wrapped past midnight, which is the whole day.
    /// </summary>
    private static TimeSpan OffsetInDay(TimeOnly start, TimeOnly end) =>
        end < start ? TimeSpan.FromDays(1) : end.ToTimeSpan();

    private sealed record Collaborators(
        AppDbContext Db,
        IReminderRegistrationService Reminders,
        IActivityTimeAttributionSink Sink,
        IUserTimeZoneResolver TimeZones);

    /// <remarks>
    /// When the time was logged <em>for</em> an item (<see cref="ActivityDayTotal.TodoListItemId"/> /
    /// <see cref="ActivityDayTotal.RoutineTodoListId"/>), that item is the only candidate, and the other kind is
    /// not touched. Otherwise the open item for the activity, oldest first: two open items sharing an activity
    /// used to be settled by whichever Postgres returned first, which is a visible wrong tick once a deliberate
    /// timer can trigger it rather than only background tracking.
    /// </remarks>
    private static async Task AutomateWithoutPlannerTaskAsync(AppDbContext dbContext, long userId, ActivityDayTotal total, CancellationToken ct)
    {
        var isLinked = total.TodoListItemId is not null || total.RoutineTodoListId is not null;

        if (!isLinked || total.TodoListItemId is not null)
            await CompleteTodoItemAsync(dbContext, userId, total, ct);

        if (!isLinked || total.RoutineTodoListId is not null)
            await CompleteRoutineItemAsync(dbContext, userId, total, ct);
    }

    private static async Task CompleteTodoItemAsync(AppDbContext dbContext, long userId, ActivityDayTotal total, CancellationToken ct)
    {
        var candidates = dbContext.Set<TodoListItem>()
            .Where(i => i.UserId == userId && !i.IsDone && i.SuggestedTime != null);
        var todoItem = await (total.TodoListItemId is { } itemId
                ? candidates.Where(i => i.Id == itemId)
                : candidates.Where(i => i.ActivityId == total.ActivityId).OrderBy(i => i.Id))
            .FirstOrDefaultAsync(ct);

        if (todoItem != null && total.TotalSecondsOnDay >= todoItem.SuggestedTime!.TotalSeconds)
        {
            todoItem.IsDone = true;
            if (todoItem.TotalCount.HasValue)
                todoItem.DoneCount = todoItem.TotalCount;
            await dbContext.SaveChangesAsync(ct);
        }
    }

    private static async Task CompleteRoutineItemAsync(AppDbContext dbContext, long userId, ActivityDayTotal total, CancellationToken ct)
    {
        var candidates = dbContext.Set<RoutineTodoList>()
            .Where(r => r.UserId == userId && !r.IsDone && r.SuggestedTime != null);
        var routineItem = await (total.RoutineTodoListId is { } itemId
                ? candidates.Where(r => r.Id == itemId)
                : candidates.Where(r => r.ActivityId == total.ActivityId).OrderBy(r => r.Id))
            .FirstOrDefaultAsync(ct);

        if (routineItem != null && total.TotalSecondsOnDay >= routineItem.SuggestedTime!.TotalSeconds)
        {
            routineItem.IsDone = true;
            if (routineItem.TotalCount.HasValue)
                routineItem.DoneCount = routineItem.TotalCount;
            RoutineResetService.UpdateItemStreak(routineItem, DateTime.UtcNow);
            await dbContext.SaveChangesAsync(ct);
        }
    }
}
