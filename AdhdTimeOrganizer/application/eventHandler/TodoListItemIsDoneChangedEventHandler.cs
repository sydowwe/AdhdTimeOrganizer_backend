using AdhdTimeOrganizer.application.@event;
using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.domain.model.@enum;
using AdhdTimeOrganizer.infrastructure.persistence;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.application.eventHandler;

public class TodoListItemIsDoneChangedEventHandler(IServiceScopeFactory scopeFactory) : IEventHandler<TodoListItemIsDoneChangedEvent>
{
    public async Task HandleAsync(TodoListItemIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var tasks = await dbContext.Set<PlannerTask>()
            .Include(pt => pt.Calendar)
            .Where(pt => pt.TodolistItemId == eventModel.TodoListItemId && pt.Calendar.Date == today)
            .ToListAsync(ct);

        if (tasks.Count == 0)
            return;

        foreach (var task in tasks)
        {
            task.Status = eventModel.NewIsDone ? PlannerTaskStatus.Completed : PlannerTaskStatus.NotStarted;
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
