using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.infrastructure.persistence;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.eventHandler;

public class PlannerTaskIsDoneChangedEventHandler(IServiceScopeFactory scopeFactory, ILogger<PlannerTaskIsDoneChangedEventHandler> logger) : IEventHandler<PlannerTaskIsDoneChangedEvent>
{
    public async Task HandleAsync(PlannerTaskIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            await SyncRoutineTodoList(dbContext, eventModel, ct);
            await SyncTodoListItem(dbContext, eventModel, ct);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The PlannerTask status change already committed before this handler runs (see
            // PatchPlannerTaskStatusEndpoint), so a failure here must not surface as a 500 for an
            // operation that already succeeded — it only leaves the linked to-do/routine item unsynced.
            logger.LogError(ex, "Failed to sync linked to-do/routine item for PlannerTask activity {ActivityId}", eventModel.ActivityId);
        }
    }

    private static async Task SyncRoutineTodoList(AppDbContext dbContext, PlannerTaskIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        var item = await dbContext.Set<RoutineTodoList>()
            .Include(r => r.RoutineTimePeriod)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.ActivityId == eventModel.ActivityId && r.UserId == eventModel.UserId, ct);

        if (item == null)
            return;

        var now = DateTime.UtcNow;

        // The same lazy single-item reset the toggle endpoints run in BeforeToggle. Without it a planner un-tick
        // landing after the period elapsed but before any reset ran would walk back a score that belongs to the
        // closed cycle, and a tick would score a cycle the item had already missed. A lost race against another
        // reset site is a DbUpdateException on save, which the caller's catch logs like any other sync failure.
        RoutineResetService.CheckGrace(item.RoutineTimePeriod, now);
        RoutineResetService.TryReset(item.RoutineTimePeriod, item, now);

        // In both directions: un-ticking the planner task is an un-tick of the routine item, and the streak
        // walk-back UpdateItemStreak does for the toggle endpoints has to happen here too.
        item.SetDone(eventModel.NewIsDone);
        RoutineResetService.UpdateItemStreak(item, now);
    }

    private static async Task SyncTodoListItem(AppDbContext dbContext, PlannerTaskIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        if (eventModel.TodoListItemId == null)
            return;

        var item = await dbContext.Set<TodoListItem>()
            .Include(i => i.Steps)
            .FirstOrDefaultAsync(i => i.Id == eventModel.TodoListItemId && i.UserId == eventModel.UserId, ct);

        if (item == null)
            return;

        item.SetDone(eventModel.NewIsDone);
    }
}