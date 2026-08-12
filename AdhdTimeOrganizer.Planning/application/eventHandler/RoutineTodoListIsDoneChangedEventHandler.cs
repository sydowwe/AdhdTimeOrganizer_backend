using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using FastEndpoints;

namespace AdhdTimeOrganizer.Planning.application.eventHandler;

/// <summary>
/// Follows a routine to-do list's done state onto today's planner tasks for the same activity.
/// </summary>
/// <remarks>
/// <para>
/// Lives in Planning, not the host: the publisher is Routines but every row this touches is a
/// <see cref="PlannerTask"/>, so the reaction belongs to the slice that owns it. Neither slice
/// references the other — <see cref="RoutineTodoListIsDoneChangedEvent"/> is declared in Core.
/// </para>
/// <para>
/// Takes a plain <c>DbContext</c> (aliased to <c>AppDbContext</c> by the host) rather than a concrete
/// one, like everything else in this project. It resolves that from a fresh scope because the
/// publisher's own scope is not guaranteed to outlive the dispatch.
/// </para>
/// </remarks>
public class RoutineTodoListIsDoneChangedEventHandler(IServiceScopeFactory scopeFactory) : IEventHandler<RoutineTodoListIsDoneChangedEvent>
{
    public async Task HandleAsync(RoutineTodoListIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var tasks = await dbContext.Set<PlannerTask>()
            .Include(pt => pt.Calendar)
            .Where(pt => pt.ActivityId == eventModel.ActivityId && pt.UserId == eventModel.UserId && pt.Calendar.Date == today)
            .ToListAsync(ct);

        if (tasks.Count == 0)
            return;

        foreach (var task in tasks)
            task.Status = eventModel.NewIsDone ? PlannerTaskStatus.Completed : PlannerTaskStatus.NotStarted;

        await dbContext.SaveChangesAsync(ct);
    }
}
