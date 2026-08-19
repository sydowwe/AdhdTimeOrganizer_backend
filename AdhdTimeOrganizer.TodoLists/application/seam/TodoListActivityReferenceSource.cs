using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.TodoLists.application.seam;

/// <summary>
/// Publishes to-do items' two activity columns through Core's reference seam.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two columns, not one.</b> <c>ActivityId</c> is what the task is, and <c>PairedLeisureActivityId</c>
/// is the temptation bundle it is paired with. Both break if the activity goes away, so both are
/// counted — an item paired with the activity it is also about counts twice, per the seam's one-row-per-
/// reference rule. Only the second is nullable.
/// </para>
/// <para>
/// ⚠ <b>The collapse case lives here.</b> An item whose <c>ActivityId</c> is "Reading" and whose
/// <c>PairedLeisureActivityId</c> is "reading" — two activities being merged in the same call — must end
/// up referencing the survivor <em>once</em>, not fail and not turn into two rows. Both columns are set
/// to the survivor and the pairing is then dropped, because an item paired with itself is not a
/// temptation bundle; it is a no-op the picker would render as a confusing self-reference. That drop
/// still counts as a repoint: the reference is resolved, and the count the snackbar shows is rows moved.
/// </para>
/// <para>
/// Nothing here touches <c>IsDone</c>, so <c>TodoListItemCompletionInterceptor</c> has nothing to stamp
/// and the merge cannot disturb a day's recap.
/// </para>
/// </remarks>
public sealed class TodoListActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.TodoList;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<TodoListItem>()
            .Select(i => i.ActivityId)
            .Concat(db.Set<TodoListItem>()
                .Where(i => i.PairedLeisureActivityId != null)
                .Select(i => i.PairedLeisureActivityId!.Value));

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var items = await db.Set<TodoListItem>()
            .Where(i => mergedIds.Contains(i.ActivityId)
                        || (i.PairedLeisureActivityId != null && mergedIds.Contains(i.PairedLeisureActivityId.Value)))
            .ToListAsync(ct);

        var repointed = 0;
        foreach (var item in items)
        {
            if (mergedIds.Contains(item.ActivityId))
            {
                item.ActivityId = survivorId;
                repointed++;
            }

            if (item.PairedLeisureActivityId is { } paired && mergedIds.Contains(paired))
            {
                item.PairedLeisureActivityId = survivorId;
                repointed++;
            }

            // Collapse: pairing an item with its own activity is meaningless, and after a merge it is a
            // shape the user never created. Covers both orders — the pairing was already the survivor and
            // the task moved onto it, or the reverse.
            if (item.PairedLeisureActivityId == item.ActivityId)
                item.PairedLeisureActivityId = null;
        }

        return repointed;
    }
}
