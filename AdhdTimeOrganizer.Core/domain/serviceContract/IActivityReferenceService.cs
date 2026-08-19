using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.domain.serviceContract;

/// <summary>
/// "How many rows point at this activity, across every slice?" — and "move them all onto that one".
/// The single consumer-facing face of the <c>IActivityReferenceSource</c> seam.
/// </summary>
/// <remarks>
/// Declared as an interface rather than used as a concrete class because the Scrutor scan in
/// <c>ModuleServiceExtensions</c> registers <c>AsImplementedInterfaces()</c>: a class carrying only
/// <c>IScopedService</c> resolves as <c>IScopedService</c> and <b>not</b> as itself, so the endpoints
/// would fail to activate. Resolved as a single service — a missing registration throws at activation
/// rather than quietly reporting every activity as unreferenced.
/// </remarks>
public interface IActivityReferenceService
{
    /// <summary>
    /// Every reference held by every source, one row per reference, as a <b>composable</b> query. The
    /// activity grid correlates a <c>COUNT</c> against this inside its projection, which is what makes
    /// <c>usageCount</c> sortable in SQL rather than on a page that has already been chosen.
    /// </summary>
    IQueryable<long> ReferencingActivityIds(DbContext db);

    /// <summary>
    /// Reference counts for a known set of ids, in one round trip. Ids with no references come back
    /// present and zero, so callers never have to distinguish "absent" from "unreferenced".
    /// </summary>
    Task<Dictionary<long, int>> CountByActivityAsync(DbContext db, IReadOnlyCollection<long> activityIds, CancellationToken ct);

    /// <summary>
    /// Repoints every source's rows from <paramref name="mergedIds"/> onto <paramref name="survivorId"/>,
    /// returning the total rows moved.
    /// </summary>
    /// <remarks>
    /// ⚠ Mutates <paramref name="db"/> and does <b>not</b> save. The caller owns the transaction — that
    /// is what makes a merge all-or-nothing, which the ask makes a hard requirement because a
    /// half-applied merge leaves history split between an activity that still exists and one that does
    /// not, with no way for the user to see it or finish it.
    /// </remarks>
    Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct);
}
