using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
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
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var total in eventModel.Totals)
        {
            try
            {
                await AutomateActivityAsync(dbContext, eventModel.UserId, eventModel.Day, total, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per activity, so one bad row does not cost the rest of the batch its automation.
                logger.LogError(ex, "Failed to automate completion for activity {ActivityId} of user {UserId}",
                    total.ActivityId, eventModel.UserId);
            }
        }
    }

    private static async Task AutomateActivityAsync(AppDbContext dbContext, long userId, DateOnly day, ActivityDayTotal total, CancellationToken ct)
    {
        var plannerTask = await dbContext.Set<PlannerTask>()
            .Include(pt => pt.Calendar)
            .Where(pt => pt.UserId == userId && pt.ActivityId == total.ActivityId
                                             && pt.Calendar.Date == day
                                             && pt.Status != PlannerTaskStatus.Completed
                                             && pt.Status != PlannerTaskStatus.Cancelled)
            .FirstOrDefaultAsync(ct);

        if (plannerTask == null)
        {
            await AutomateWithoutPlannerTaskAsync(dbContext, userId, total, ct);
            return;
        }

        var durationSeconds = (int)(plannerTask.EndTime - plannerTask.StartTime).TotalSeconds;
        var wasCompleted = durationSeconds > 0 && total.TotalSecondsOnDay >= durationSeconds;

        plannerTask.Status = wasCompleted ? PlannerTaskStatus.Completed : PlannerTaskStatus.InProgress;
        await dbContext.SaveChangesAsync(ct);

        if (wasCompleted)
            await new PlannerTaskIsDoneChangedEvent(total.ActivityId, userId, true, plannerTask.TodolistItemId)
                .PublishAsync(Mode.WaitForAll, ct);
    }

    private static async Task AutomateWithoutPlannerTaskAsync(AppDbContext dbContext, long userId, ActivityDayTotal total, CancellationToken ct)
    {
        var todoItem = await dbContext.Set<TodoListItem>()
            .FirstOrDefaultAsync(i => i.UserId == userId && i.ActivityId == total.ActivityId
                                                         && !i.IsDone && i.SuggestedTime != null, ct);

        if (todoItem != null && total.TotalSecondsOnDay >= todoItem.SuggestedTime!.TotalSeconds)
        {
            todoItem.IsDone = true;
            if (todoItem.TotalCount.HasValue)
                todoItem.DoneCount = todoItem.TotalCount;
            await dbContext.SaveChangesAsync(ct);
        }

        var routineItem = await dbContext.Set<RoutineTodoList>()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.ActivityId == total.ActivityId
                                                         && !r.IsDone && r.SuggestedTime != null, ct);

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
