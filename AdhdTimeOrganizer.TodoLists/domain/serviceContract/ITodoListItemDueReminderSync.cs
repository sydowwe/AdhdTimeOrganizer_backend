namespace AdhdTimeOrganizer.TodoLists.domain.serviceContract;

/// <summary>
/// The one seam between <c>TodoListItem</c>'s due date / due time and the Reminders module's registry: every
/// write path that changes an item's due instant, its completion, its title or its existence calls this after its
/// save, so the key format, the lead offset and the register-vs-cancel decision exist exactly once. The host's
/// paths (fresh start, the completion event handlers, the preferences time-zone change) call the same service.
/// <para>
/// <b>Always call after the item's own save has committed.</b> The registry writes through the same scoped
/// <c>DbContext</c> and commits, so registering first would publish a schedule for a row that may never exist.
/// None of these methods throws for a registry failure: the item already committed and the request has nothing
/// left to roll back, so a failure is logged (ids only) and the dispatch guard is what stops a stale reminder.
/// </para>
/// <para>
/// <b>What it cannot see:</b> a database cascade. Deleting a list or an activity removes items without any of
/// them passing through here, so the list delete reads its item ids before the delete and cancels after; the
/// activity delete (in Core, which cannot see this slice) is left to the dispatch guard.
/// </para>
/// </summary>
public interface ITodoListItemDueReminderSync
{
    /// <summary>
    /// Brings each item's reminder in line with the item as stored now: registered when it is open and has a due
    /// date and time still ahead, cancelled otherwise — including when the id no longer exists. Idempotent; the
    /// registry upserts by key, so calling this after every edit is the whole re-registration story.
    /// </summary>
    Task SyncAsync(IReadOnlyCollection<long> itemIds, CancellationToken ct = default);

    /// <summary>Withdraws the reminders of items that are gone. Safe for ids that never had one.</summary>
    Task CancelAsync(IReadOnlyCollection<long> itemIds, CancellationToken ct = default);

    /// <summary>
    /// Re-syncs every open, timed item of the user's that could still be ahead — after a change that moves all of
    /// them at once: their lead setting, or their time zone.
    /// </summary>
    Task SyncAllForUserAsync(long userId, CancellationToken ct = default);
}
