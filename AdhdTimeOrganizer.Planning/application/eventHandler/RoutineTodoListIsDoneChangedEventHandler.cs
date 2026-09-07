using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.application.helper;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using FastEndpoints;

namespace AdhdTimeOrganizer.Planning.application.eventHandler;

/// <summary>
/// Follows a routine to-do list's done state onto today's planner tasks for the same activity, and
/// resyncs their reminders.
/// </summary>
/// <remarks>
/// <para>
/// Lives in Planning, not the host: the publisher is Routines but every row this touches is a
/// <see cref="PlannerTask"/>, and <see cref="IReminderRegistrationService"/> is Planning's own
/// (reminders are part of this slice). Neither slice references the other —
/// <see cref="RoutineTodoListIsDoneChangedEvent"/> is declared in Core.
/// </para>
/// <para>
/// Takes a plain <c>DbContext</c> (aliased to <c>AppDbContext</c> by the host) rather than a concrete
/// one, like everything else in this project. It resolves that from a fresh scope because the
/// publisher's own scope is not guaranteed to outlive the dispatch.
/// </para>
/// <para>
/// Deliberately the same shape as <see cref="TodoListItemIsDoneChangedEventHandler"/> — the two are
/// both "a parent toggle fans out onto today's planner tasks", and every rule below is a product
/// ruling about the user's tasks, not about which slice published the event.
/// </para>
/// </remarks>
public class RoutineTodoListIsDoneChangedEventHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<RoutineTodoListIsDoneChangedEventHandler> logger) : IEventHandler<RoutineTodoListIsDoneChangedEvent>
{
    public async Task HandleAsync(RoutineTodoListIsDoneChangedEvent eventModel, CancellationToken ct)
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

            var tasks = await dbContext.Set<PlannerTask>()
                .Include(pt => pt.Calendar)
                .Where(pt => pt.ActivityId == eventModel.ActivityId && pt.UserId == eventModel.UserId && pt.Calendar.Date == today)
                .ToListAsync(ct);

            if (tasks.Count == 0)
                return;

            // A task the user deliberately cancelled that day is left alone — the parent routine toggle
            // must not silently un-cancel or re-complete it, exactly as the to-do sibling does not.
            var changedTasks = tasks.Where(t => t.Status != PlannerTaskStatus.Cancelled).ToList();
            if (changedTasks.Count == 0)
                return;

            // ApplyStatus, not a bare Status assignment: it is the shared reset that also clears the
            // actual times, which the plan-vs-actual trend would otherwise keep measuring.
            foreach (var task in changedTasks)
                task.ApplyStatus(eventModel.NewIsDone ? PlannerTaskStatus.Completed : PlannerTaskStatus.NotStarted);

            await dbContext.SaveChangesAsync(ct);

            // Mirrors PatchPlannerTaskStatusEndpoint: a completed/reverted task must not keep nudging
            // (or fail to keep nudging) the user about work whose status just changed underneath it.
            await reminders.SyncForPlannerTasksAsync(changedTasks.Select(t => t.Id).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // RoutineToggleIsDoneTodoListEndpoint awaits this via PublishAsync(Mode.WaitForAll, …) after
            // its own commit, so a failure here (e.g. a concurrent PatchPlannerTaskStatusEndpoint call
            // throwing DbUpdateConcurrencyException) must not surface as a 500 for an unrelated endpoint.
            logger.LogError(ex, "Failed to sync PlannerTasks for Activity {ActivityId} of user {UserId}",
                eventModel.ActivityId, eventModel.UserId);
        }
    }
}
