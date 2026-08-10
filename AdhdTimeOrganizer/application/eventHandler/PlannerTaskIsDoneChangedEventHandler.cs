using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using AdhdTimeOrganizer.Routines.domain.service;
using AdhdTimeOrganizer.infrastructure.persistence;
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
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.ActivityId == eventModel.ActivityId && r.UserId == eventModel.UserId, ct);

        if (item == null)
            return;

        if (eventModel.NewIsDone)
        {
            item.SetDone(true);
            RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);
        }
        else
        {
            item.SetDone(false);
        }
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