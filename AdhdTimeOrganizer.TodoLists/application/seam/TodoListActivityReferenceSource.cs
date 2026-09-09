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
/// ⚠ <b>So does the row collision.</b> <c>(UserId, ActivityId, TodoListId)</c> is unique
/// (<c>TodoListItemConfiguration</c>), so two items on <em>one</em> list — one per merged activity, which
/// is the ordinary shape of a duplicate — cannot both repoint onto the survivor. Left alone the index
/// rejects the second row and the whole merge transaction rolls back, so the user can never merge the
/// duplicates the feature exists for. <see cref="CollapseListCollisionsAsync"/> keeps one item per
/// <c>(UserId, TodoListId)</c> — the survivor's own pre-existing item where there is one, else the lowest
/// id — and deletes the rest. A deleted item still counts as repointed, same rule as the dropped pairing.
/// </para>
/// <para>
/// Nothing here touches <c>IsDone</c>, so <c>TodoListItemCompletionInterceptor</c> has nothing to stamp
/// and the merge cannot disturb a day's recap. Deleting a colliding item does reach outside this slice —
/// <c>ActivityHistory.TodoListItemId</c> is <c>SetNull</c> and <c>PlannerTask.TodolistItemId</c> points
/// here — so recorded time survives the collapse rather than going with the row.
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
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        var repointed = 0;
        var moved = new List<TodoListItem>();
        foreach (var item in items)
        {
            if (mergedIds.Contains(item.ActivityId))
            {
                item.ActivityId = survivorId;
                moved.Add(item);
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

        await CollapseListCollisionsAsync(db, survivorId, moved, ct);

        return repointed;
    }

    /// <summary>
    /// Resolves the unique <c>(UserId, ActivityId, TodoListId)</c> index the repointing above would
    /// otherwise violate: at most one item per list can carry the survivor, and the losers are deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keeper is the survivor's own pre-existing item where it has one — the row the user chose to
    /// keep, matching <c>ActivityProfileActivityReferenceSource</c>'s "the survivor's own profile wins" —
    /// and otherwise the lowest id among the repointed ones, so that folding several duplicates in keeps
    /// the item the user has had longest rather than whichever row Postgres returned first.
    /// </para>
    /// <para>
    /// Items with a null <c>TodoListId</c> are skipped: Postgres indexes nulls as distinct, so an item on
    /// no list never collides with anything.
    /// </para>
    /// <para>
    /// The incumbent lookup is scoped by <c>UserId</c> by hand rather than leaning on the ambient
    /// per-user filter — the seam takes a plain <c>DbContext</c> and cannot assume one is applied, and a
    /// missed incumbent is exactly the row the index then rejects. The repointed items are invisible to
    /// this query on purpose: their new <c>ActivityId</c> lives only in the change tracker, so the SQL
    /// matches the survivor's stored rows and nothing else.
    /// </para>
    /// </remarks>
    private static async Task CollapseListCollisionsAsync(DbContext db, long survivorId, List<TodoListItem> moved, CancellationToken ct)
    {
        var onLists = moved.Where(i => i.TodoListId != null).ToList();
        if (onLists.Count == 0)
            return;

        var userIds = onLists.Select(i => i.UserId).Distinct().ToList();
        var listIds = onLists.Select(i => i.TodoListId!.Value).Distinct().ToList();

        var incumbents = await db.Set<TodoListItem>()
            .Where(i => i.ActivityId == survivorId
                        && userIds.Contains(i.UserId)
                        && i.TodoListId != null
                        && listIds.Contains(i.TodoListId.Value))
            .ToListAsync(ct);

        foreach (var group in onLists.GroupBy(i => (i.UserId, TodoListId: i.TodoListId!.Value)))
        {
            var incumbent = incumbents.FirstOrDefault(
                i => i.UserId == group.Key.UserId && i.TodoListId == group.Key.TodoListId);

            var keeper = incumbent ?? group.First();

            foreach (var loser in group.Where(i => !ReferenceEquals(i, keeper)))
                db.Set<TodoListItem>().Remove(loser);
        }
    }
}
