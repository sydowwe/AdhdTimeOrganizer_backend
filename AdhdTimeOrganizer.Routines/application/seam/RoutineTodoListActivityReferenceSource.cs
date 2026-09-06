using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Routines.application.seam;

/// <summary>
/// Publishes routines' activity column through Core's reference seam.
/// </summary>
/// <remarks>
/// <para>
/// A routine is the entity most worth protecting from a hard delete after history itself: it carries
/// <c>Streak</c> and <c>BestStreak</c>, which are the only record of a habit the user has been keeping
/// and which no amount of re-creating the activity brings back.
/// </para>
/// <para>
/// Streaks are carried across a merge untouched, and two routines on two duplicate activities stay two
/// routines pointing at one activity — merging says the activities were the same thing, not that the
/// habits were, and silently folding two streaks into one would have to invent a rule (max? sum? most
/// recent?) that no one asked for and that would quietly destroy the other. <b>That holds only while the
/// two routines sit in different time periods</b>, which the schema is what decides:
/// <c>(UserId, TimePeriodId, ActivityId)</c> is unique, so two routines in the <em>same</em> period
/// cannot both point at the survivor. See the collision rule on <see cref="RepointAsync"/>.
/// </para>
/// <para>
/// Unlike TodoLists there is no second column here: <c>RoutineTodoList</c> extends
/// <c>BaseTodoListItem</c> but not <c>TodoListItem</c>, so it has no temptation pairing.
/// </para>
/// </remarks>
public sealed class RoutineTodoListActivityReferenceSource : IActivityReferenceSource, IScopedService
{
    public string Key => ActivityReferenceSourceKeys.RoutineTodoList;

    public IQueryable<long> ReferencingActivityIds(DbContext db) =>
        db.Set<RoutineTodoList>().Select(r => r.ActivityId);

    /// <summary>
    /// Repoints every routine on a merged activity, honouring the unique
    /// <c>(UserId, TimePeriodId, ActivityId)</c>: at most one routine per period can end up on the
    /// survivor, and the others are deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The survivor's own routine wins</b> — its streak is the one the user has been looking at, and
    /// the survivor is the row they chose to keep. Where the survivor has no routine in that period the
    /// oldest merged one is repointed and survives, so folding a long-running duplicate into a survivor
    /// that never had the habit keeps the streak rather than discarding it.
    /// </para>
    /// <para>
    /// This is the one place a merge destroys a streak, which is why it is a rule stated here rather than
    /// a <c>DbUpdateException</c> failing the whole six-slice merge on the index. Nothing is folded: the
    /// losing streak is dropped, not maxed or summed, for the reason in the type remarks. Recorded time
    /// is not lost with it — <c>ActivityHistory.RoutineTodoListId</c> is <c>SetNull</c>, and the history
    /// rows' own <c>ActivityId</c> is repointed by History's source in the same transaction.
    /// </para>
    /// </remarks>
    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var rows = await db.Set<RoutineTodoList>()
            .Where(r => mergedIds.Contains(r.ActivityId))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return 0;

        var survivorSlots = (await db.Set<RoutineTodoList>()
                .Where(r => r.ActivityId == survivorId)
                .Select(r => new { r.UserId, r.TimePeriodId })
                .ToListAsync(ct))
            .Select(s => (s.UserId, s.TimePeriodId))
            .ToHashSet();

        foreach (var group in rows.GroupBy(r => (r.UserId, r.TimePeriodId)))
        {
            // Ordered by id above, so when the survivor holds no routine in this period and several
            // merged activities do, the oldest wins — the habit the user has kept longest — rather than
            // whichever row Postgres happened to return first.
            var keeper = survivorSlots.Contains(group.Key) ? null : group.First();

            foreach (var row in group)
            {
                if (ReferenceEquals(row, keeper))
                    row.ActivityId = survivorId;
                else
                    db.Set<RoutineTodoList>().Remove(row);
            }
        }

        // A row deleted to resolve a collision counts as repointed; see the seam's remarks.
        return rows.Count;
    }
}
