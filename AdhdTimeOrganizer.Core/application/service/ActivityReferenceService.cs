using AdhdTimeOrganizer.Core.application.seam;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Core.application.service;

/// <summary>
/// Folds every <see cref="IActivityReferenceSource"/> into the two operations A9 needs: a composable
/// "how many rows point at this activity" subquery, and an atomic repoint across all of them.
/// </summary>
/// <remarks>
/// <para>
/// The interesting half is <see cref="ReferencingActivityIds"/>. It <c>Concat</c>s the sources into a
/// single <c>UNION ALL</c> that stays an <see cref="IQueryable{T}"/>, so the grid can correlate a
/// <c>COUNT</c> against it <em>inside</em> its projection. That is what makes <c>usageCount</c> sortable
/// — <c>BaseGridEndpoint</c> applies <c>SortByMany</c> to the projected queryable, so a field filled in
/// by <c>PostProcessItemsAsync</c> would sort every row on <c>0</c> and silently return the wrong page.
/// </para>
/// <para>
/// It is not cheap: one correlated count over a twelve-table <c>UNION ALL</c> per row of the page. It is
/// affordable here because every table involved is user-scoped by the global query filter and pages are
/// capped at 200 rows. If that stops being true, the fallback the frontend already agreed to is
/// <c>canDelete</c> everywhere and <c>usageCount</c> on <c>GET /activity/{id}</c> only — at which point
/// the grid column loses its sort and this method loses its reason to be composable.
/// </para>
/// </remarks>
public sealed class ActivityReferenceService(IEnumerable<IActivityReferenceSource> sources)
    : IActivityReferenceService, IScopedService
{
    private readonly IReadOnlyList<IActivityReferenceSource> _sources = sources.ToList();

    /// <summary>
    /// Every reference held by every source, one row per reference. Compose it; never materialize it.
    /// </summary>
    /// <remarks>
    /// Returns an empty queryable when no source resolved, which is the silent-failure shape this seam
    /// shares with <see cref="IActivityMembershipSource"/>: nothing throws, <c>usageCount</c> just comes
    /// back <c>0</c> and every activity looks deletable. <c>SeamWiringTests</c> is the guard, because no
    /// runtime check here can tell "genuinely unreferenced" from "the slice never registered".
    /// </remarks>
    public IQueryable<long> ReferencingActivityIds(DbContext db)
    {
        if (_sources.Count == 0)
            return Enumerable.Empty<long>().AsQueryable();

        return _sources
            .Select(s => s.ReferencingActivityIds(db))
            .Aggregate((all, next) => all.Concat(next));
    }

    /// <summary>
    /// Counts references for a known set of activity ids in one round trip. Used by the single-row reads
    /// (<c>GET /activity/{id}</c>) and by the merge endpoint's response, where the ids are already known
    /// and correlating per row would be pointless.
    /// </summary>
    public async Task<Dictionary<long, int>> CountByActivityAsync(DbContext db, IReadOnlyCollection<long> activityIds, CancellationToken ct)
    {
        var counts = activityIds.ToDictionary(id => id, _ => 0);
        if (activityIds.Count == 0 || _sources.Count == 0)
            return counts;

        var grouped = await ReferencingActivityIds(db)
            .Where(id => activityIds.Contains(id))
            .GroupBy(id => id)
            .Select(g => new { ActivityId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        foreach (var row in grouped)
            counts[row.ActivityId] = row.Count;

        return counts;
    }

    /// <summary>
    /// Repoints every source's rows from <paramref name="mergedIds"/> onto <paramref name="survivorId"/>
    /// and returns the total rows moved.
    /// </summary>
    /// <remarks>
    /// ⚠ Mutates <paramref name="db"/> and does not save — the caller owns the transaction, which is what
    /// makes the merge atomic. Sources run sequentially rather than in parallel because they share one
    /// <see cref="DbContext"/>, which is not thread-safe.
    /// </remarks>
    public async Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct)
    {
        var repointed = 0;
        foreach (var source in _sources)
            repointed += await source.RepointAsync(db, survivorId, mergedIds, ct);

        return repointed;
    }
}
