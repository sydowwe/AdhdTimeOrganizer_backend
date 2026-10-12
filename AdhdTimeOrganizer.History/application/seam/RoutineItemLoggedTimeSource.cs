using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.History.application.seam;

/// <summary>
/// Reports the recordings logged against routine items out of <see cref="ActivityHistory"/> through Core's seam, so
/// Routines can learn each item's typical length (F2-06) without referencing History. The twin of
/// <see cref="TodoListItemLoggedTimeSource"/>.
/// </summary>
public sealed class RoutineItemLoggedTimeSource : IRoutineItemLoggedTimeSource, IScopedService
{
    public async Task<IReadOnlyList<RoutineItemLoggedTime>> LoggedTimeBetweenAsync(
        DbContext db, long userId, IReadOnlyCollection<long> routineTodoListIds, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct)
    {
        if (routineTodoListIds.Count == 0)
            return [];

        var ids = routineTodoListIds.Distinct().ToList();

        // Filtered on UserId explicitly even though AppDbContext's global filter already scopes ActivityHistory to
        // the ambient user: the caller passes the id it authenticated, and this must not report against whoever
        // happens to be ambient.
        //
        // Keyed on RoutineTodoListId alone — never widened to "any time logged against this item's activity". Two
        // routine items may share one activity, and that fallback would credit the same seconds to both.
        var rows = await db.Set<ActivityHistory>()
            .Where(h => h.UserId == userId
                        && h.RoutineTodoListId != null
                        && ids.Contains(h.RoutineTodoListId.Value)
                        && h.StartTimestamp >= fromUtc && h.StartTimestamp < toUtc)
            .Select(h => new { ItemId = h.RoutineTodoListId!.Value, h.StartTimestamp, h.Length })
            .ToListAsync(ct);

        // Length is an IntTime behind a converter, so its TotalSeconds is read here rather than in SQL.
        return rows
            .Select(r => new RoutineItemLoggedTime(r.ItemId, r.StartTimestamp, r.Length.TotalSeconds))
            .ToList();
    }
}
