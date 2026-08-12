using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.interceptor;

/// <summary>
/// Stamps <see cref="TodoListItem.CompletedTimestamp"/> whenever an item's <c>IsDone</c> actually
/// flips, and clears it when the item is un-done.
/// </summary>
/// <remarks>
/// <para>
/// This lives in an interceptor rather than at the call sites because there are five of them —
/// <c>BaseToggleIsDoneTodoListEndpoint</c>, <c>BaseToggleStepIsDoneEndpoint</c>,
/// <c>BaseTodoListItem.SetDone</c>, <c>UpdateTodoListItemRequest</c> and the host's
/// <c>ActivityTimeRecordedEventHandler</c> — and a sixth is one feature away. A site that forgot to
/// stamp would break nothing loudly: the save succeeds, the item shows as done, and it merely never
/// appears in a daily recap. Keying off the ChangeTracker instead means no writer can miss it and
/// none of them has to know the rule exists.
/// </para>
/// <para>
/// ⚠ Stamped in <c>SavingChangesAsync</c>, not after the fact, so the value lands in the same
/// <c>UPDATE</c> as the <c>IsDone</c> it describes and rolls back with it. Anything that bypasses the
/// ChangeTracker — <c>ExecuteUpdateAsync</c> in particular — bypasses this too, which is the usual
/// reason to keep <c>IsDone</c> writes on tracked entities.
/// </para>
/// <para>
/// Registered host-side in <c>Program.cs</c> alongside <c>SuggestionPatternRefreshInterceptor</c>.
/// Nothing fails if that registration is dropped; every recap simply comes back empty, which is what
/// <c>TodoListItemDailyRecapTests.Completing_StampsTheTimestamp</c> is there to catch.
/// </para>
/// </remarks>
public sealed class TodoListItemCompletionInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        // The synchronous path exists because SaveChanges() is not routed through SaveChangesAsync().
        // Seeders and a couple of dev paths still call it, and an item completed there would go
        // unstamped.
        Stamp(eventData.Context);
        return result;
    }

    private static void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        // DateTime.UtcNow directly, as the toggle endpoints already do — this solution registers no
        // TimeProvider, and introducing one here would be the only injected clock in the codebase.
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<TodoListItem>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // An item created already-done — the seeders do this — still needs a timestamp,
                    // and has no original value to compare against.
                    if (entry.Entity.IsDone && entry.Entity.CompletedTimestamp is null)
                        entry.Entity.CompletedTimestamp = now;
                    break;

                case EntityState.Modified:
                    var isDone = entry.Property(e => e.IsDone);

                    // Only on an actual transition. Re-stamping on every save that touched an
                    // already-done item would walk the completion date forward each time a note or a
                    // due date was edited, quietly moving finished tasks into today's recap.
                    if (!isDone.IsModified || Equals(isDone.OriginalValue, isDone.CurrentValue))
                        break;

                    entry.Entity.CompletedTimestamp = entry.Entity.IsDone ? now : null;
                    break;
            }
        }
    }
}
