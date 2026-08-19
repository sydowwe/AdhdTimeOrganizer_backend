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
/// Streaks are carried across a merge untouched. Two routines on two duplicate activities stay two
/// routines pointing at one activity — merging says the activities were the same thing, not that the
/// habits were, and silently folding two streaks into one would have to invent a rule (max? sum? most
/// recent?) that no one asked for and that would quietly destroy the other.
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

    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var rows = await db.Set<RoutineTodoList>()
            .Where(r => mergedIds.Contains(r.ActivityId))
            .ToListAsync(ct);

        foreach (var row in rows)
            row.ActivityId = survivorId;

        return rows.Count;
    }
}
