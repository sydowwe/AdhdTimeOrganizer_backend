using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;

namespace AdhdTimeOrganizer.Planning.application.eventHandler;

/// <summary>
/// Follows a to-do item's done state onto today's planner tasks linked to it, and resyncs their
/// reminders.
/// </summary>
/// <remarks>
/// <para>
/// Lives in Planning, not the host: the publisher is TodoLists but every row this touches is a
/// <see cref="PlannerTask"/>, and <see cref="IReminderRegistrationService"/> is Planning's own
/// (reminders are part of this slice). Neither slice references the other —
/// <see cref="TodoListItemIsDoneChangedEvent"/> is declared in Core, and the join is via
/// <c>PlannerTask.TodolistItemId</c>, the navigation-free FK the host declares.
/// </para>
/// </remarks>
public class TodoListItemIsDoneChangedEventHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<TodoListItemIsDoneChangedEventHandler> logger) : IEventHandler<TodoListItemIsDoneChangedEvent>
{
    public async Task HandleAsync(TodoListItemIsDoneChangedEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var reminders = scope.ServiceProvider.GetRequiredService<IReminderRegistrationService>();
        var timeZones = scope.ServiceProvider.GetRequiredService<IUserTimeZoneResolver>();

        try
        {
            // The user's today, not the server's — see TaskPlannerHelper.TodayIn. Resolved from this scope's
            // DbContext, so it works under a background dispatch where there is no HttpContext to read.
            var today = TaskPlannerHelper.TodayIn(await timeZones.GetAsync(eventModel.UserId, ct));

            // Scoped on the event's UserId, not on the ambient query filter. The filter resolves to null
            // whenever there is no authenticated principal (BaseDbContext.ScopeUserId) — a scheduled job, a
            // seeder, a future background fan-out — and a no-op filter would fan this out across every user
            // holding a task linked to that item id. Same predicate shape as the routine sibling.
            var tasks = await dbContext.Set<PlannerTask>()
                .Include(pt => pt.Calendar)
                .Where(pt => pt.TodolistItemId == eventModel.TodoListItemId
                             && pt.UserId == eventModel.UserId
                             && pt.Calendar.Date == today)
                .ToListAsync(ct);

            if (tasks.Count == 0)
                return;

            // A task the user deliberately cancelled that day is left alone — the parent to-do toggle
            // must not silently un-cancel or re-complete it.
            var changedTasks = tasks.Where(t => t.Status != PlannerTaskStatus.Cancelled).ToList();
            foreach (var task in changedTasks)
                task.ApplyStatus(eventModel.NewIsDone ? PlannerTaskStatus.Completed : PlannerTaskStatus.NotStarted);

            if (changedTasks.Count == 0)
                return;

            await dbContext.SaveChangesAsync(ct);

            // Mirrors PatchPlannerTaskStatusEndpoint: a completed/reverted task must not keep nudging
            // (or fail to keep nudging) the user about work whose status just changed underneath it.
            await reminders.SyncForPlannerTasksAsync(changedTasks.Select(t => t.Id).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ToggleIsDoneTodoListItemEndpoint awaits this via PublishAsync(Mode.WaitForAll, …) after its
            // own commit, so a failure here (e.g. a concurrent PatchPlannerTaskStatusEndpoint call
            // throwing DbUpdateConcurrencyException) must not surface as a 500 for an unrelated endpoint.
            logger.LogError(ex, "Failed to sync PlannerTasks for TodoListItem {TodoListItemId} of user {UserId}",
                eventModel.TodoListItemId, eventModel.UserId);
        }
    }
}
