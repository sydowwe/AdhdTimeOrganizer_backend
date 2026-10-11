using AdhdTimeOrganizer.Core.application.@event;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using FastEndpoints;

namespace AdhdTimeOrganizer.TodoLists.application.eventHandler;

/// <summary>
/// Makes the due reminders of an activity's to-dos follow the activity's archived flag. An archived activity's
/// items drop out of every to-do read, and out of the reminder sync's own read with them, so re-syncing them is the
/// whole job in both directions: on archive the sync finds them hidden and withdraws their reminders — off
/// <c>my-upcoming</c> now, not when the dispatch guard would have retired them at fire time — and on restore it
/// finds them again and re-registers whichever are still ahead.
/// <para>
/// Lives in TodoLists, not the host: every row it reads and every registration it moves is this slice's. Runs
/// after Core's commit in its own scope, and logs and swallows like every other seam handler — the archive has
/// already happened.
/// </para>
/// </summary>
public class ActivityArchivedChangedEventHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<ActivityArchivedChangedEventHandler> logger) : IEventHandler<ActivityArchivedChangedEvent>
{
    public async Task HandleAsync(ActivityArchivedChangedEvent eventModel, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        var dueReminders = scope.ServiceProvider.GetRequiredService<ITodoListItemDueReminderSync>();

        try
        {
            // Scoped on the event's UserId rather than the ambient filter (see the event's remarks). Only timed,
            // open items can have a reminder; the sync decides the rest.
            var itemIds = await dbContext.Set<TodoListItem>()
                .Where(i => i.ActivityId == eventModel.ActivityId
                            && i.UserId == eventModel.UserId
                            && !i.IsDone
                            && i.DueTime != null)
                .Select(i => i.Id)
                .ToListAsync(ct);

            await dueReminders.SyncAsync(itemIds, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to re-sync the to-do due reminders for activity {ActivityId} after it was {Action}",
                eventModel.ActivityId, eventModel.IsArchived ? "archived" : "restored");
        }
    }
}
