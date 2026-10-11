using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using AdhdTimeOrganizer.TodoLists.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.TodoLists.application.seam;

/// <summary>
/// Keeps the to-do due reminders honest when an activity goes away. Before the removal it reads which timed, open
/// items sit on the activities; after the commit it re-syncs exactly those ids. That one call is right for both
/// removals: an item the delete cascaded away — or the merge collapsed into a duplicate — no longer exists, so its
/// reminder is cancelled; an item the merge repointed onto the survivor still does, so it is re-registered and its
/// label follows the survivor's name.
/// <para>
/// Without it the reminder would sit on the user's <c>my-upcoming</c> list until its instant, where the dispatch
/// guard would retire it — never sent, but listed for something that no longer exists.
/// </para>
/// </summary>
public sealed class TodoListActivityRemovalParticipant(ITodoListItemDueReminderSync dueReminders)
    : IActivityRemovalParticipant, IScopedService
{
    public string Key => ActivityRemovalParticipantKeys.TodoList;

    public async Task<Func<CancellationToken, Task>?> BeforeRemovalAsync(
        DbContext db, IReadOnlyCollection<long> activityIds, CancellationToken ct)
    {
        // Under the caller's own filters: the activities are the caller's, so are the items on them.
        var itemIds = await db.Set<TodoListItem>()
            .AsNoTracking()
            .Where(i => activityIds.Contains(i.ActivityId) && !i.IsDone && i.DueTime != null)
            .Select(i => i.Id)
            .ToListAsync(ct);

        return itemIds.Count == 0 ? null : token => dueReminders.SyncAsync(itemIds, token);
    }
}
