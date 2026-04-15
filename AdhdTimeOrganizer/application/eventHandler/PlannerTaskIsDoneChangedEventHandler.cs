using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.domain.service;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.eventHandler;

public class PlannerTaskIsDoneChangedEventHandler(IServiceScopeFactory scopeFactory) : IEventHandler<PlannerTaskIsDoneChangedEvent>
{
    public async Task HandleAsync(PlannerTaskIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SyncRoutineTodoList(dbContext, eventModel, ct);
        await SyncTodoListItem(dbContext, eventModel, ct);
    }

    private static async Task SyncRoutineTodoList(AppDbContext dbContext, PlannerTaskIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        var item = await dbContext.Set<RoutineTodoList>()
            .FirstOrDefaultAsync(r => r.ActivityId == eventModel.ActivityId && r.UserId == eventModel.UserId, ct);

        if (item == null)
            return;

        if (eventModel.NewIsDone)
        {
            item.IsDone = true;
            if (item.TotalCount.HasValue)
                item.DoneCount = item.TotalCount;
            RoutineResetService.UpdateItemStreak(item, DateTime.UtcNow);
        }
        else
        {
            item.IsDone = false;
            if (item.TotalCount.HasValue)
                item.DoneCount = 0;
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private static async Task SyncTodoListItem(AppDbContext dbContext, PlannerTaskIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        if (eventModel.TodoListItemId == null)
            return;

        var item = await dbContext.Set<TodoListItem>()
            .FirstOrDefaultAsync(i => i.Id == eventModel.TodoListItemId, ct);

        if (item == null)
            return;

        if (eventModel.NewIsDone)
        {
            item.IsDone = true;
            if (item.TotalCount.HasValue)
                item.DoneCount = item.TotalCount;
        }
        else
        {
            item.IsDone = false;
            if (item.TotalCount.HasValue)
                item.DoneCount = 0;
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
