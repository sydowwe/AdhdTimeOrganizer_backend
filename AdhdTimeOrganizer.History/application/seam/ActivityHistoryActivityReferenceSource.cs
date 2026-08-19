using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.History.application.seam;

/// <summary>
/// Publishes recorded time as an activity reference, so Core can count it and repoint it without
/// referencing History.
/// </summary>
/// <remarks>
/// <para>
/// These are the rows the whole archive feature exists to protect: <c>ActivityHistory</c> is the source
/// of truth for time the user actually spent, the FK is <c>DeleteBehavior.Cascade</c>, and until A9 the
/// only lifecycle operation on offer was the delete that silently took a year of it away.
/// </para>
/// <para>
/// Merging repoints rather than deduplicates. Two history rows on the same activity and the same day are
/// legitimate — the ledger records recordings, not daily totals — so nothing here collapses them, and
/// the dashboards that group by activity will simply see one activity where they used to see two. That
/// is the point of the merge.
/// </para>
/// <para>
/// The two nullable item links (<c>TodoListItemId</c> / <c>RoutineTodoListId</c>) are untouched: they
/// say which task a recording was saved from, and merging activities changes nothing about that.
/// </para>
/// </remarks>
public sealed class ActivityHistoryActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.ActivityHistory;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<ActivityHistory>().Select(h => h.ActivityId);

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        // Tracked rather than ExecuteUpdateAsync: this runs inside the merge endpoint's transaction, and a
        // set-based update would bypass the ChangeTracker the caller's single SaveChanges relies on.
        var rows = await db.Set<ActivityHistory>()
            .Where(h => mergedIds.Contains(h.ActivityId))
            .ToListAsync(ct);

        foreach (var row in rows)
            row.ActivityId = survivorId;

        return rows.Count;
    }
}
