using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.History.application.seam;

/// <summary>
/// Reports logged time per to-do item out of <see cref="ActivityHistory"/> through Core's seam, so
/// TodoLists' daily recap can show it without referencing History.
/// </summary>
public sealed class TodoListItemLoggedTimeSource : ITodoListItemLoggedTimeSource, IScopedService
{
    public async Task<IReadOnlyDictionary<long, long>> LoggedSecondsBetweenAsync(
        DbContext db, long userId, IReadOnlyCollection<long> todoListItemIds, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct)
    {
        if (todoListItemIds.Count == 0)
            return new Dictionary<long, long>();

        var ids = todoListItemIds.Distinct().ToList();

        // Filtered on UserId explicitly even though AppDbContext's global filter already scopes
        // ActivityHistory to the ambient user: the caller passes the id it authenticated, and this
        // must not report against whoever happens to be ambient.
        //
        // Keyed on TodoListItemId alone — never widened to "any time logged against this item's
        // activity". Two items may share one activity, and that fallback would credit the same
        // seconds to both. See the seam's remarks.
        var rows = await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId
                        && h.TodoListItemId != null
                        && ids.Contains(h.TodoListItemId.Value)
                        && h.StartTimestamp >= fromUtc && h.StartTimestamp < toUtc)
            .Select(h => new { ItemId = h.TodoListItemId!.Value, h.Length })
            .ToListAsync(ct);

        // Summed in memory, as everywhere else in this slice: Length is an IntTime value object
        // behind a converter, so its TotalSeconds is not translatable to SQL. Only the two columns
        // the aggregate needs are projected, so the transfer is a pair of ints per row.
        return rows
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(r => (long)r.Length.TotalSeconds));
    }

    public async Task<IReadOnlyDictionary<long, DateTime>> LatestStartBetweenAsync(
        DbContext db, long userId, IReadOnlyCollection<long> activityIds, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct)
    {
        if (activityIds.Count == 0)
            return new Dictionary<long, DateTime>();

        var ids = activityIds.Distinct().ToList();

        // UserId filtered explicitly for the same reason as above. No Activity join, so the archived
        // filter on Activity never applies: a reward taken on an activity archived since still counts.
        return await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId
                        && ids.Contains(h.ActivityId)
                        && h.StartTimestamp >= fromUtc && h.StartTimestamp < toUtc)
            .GroupBy(h => h.ActivityId)
            .Select(g => new { ActivityId = g.Key, LatestStart = g.Max(h => h.StartTimestamp) })
            .ToDictionaryAsync(r => r.ActivityId, r => r.LatestStart, ct);
    }
}
