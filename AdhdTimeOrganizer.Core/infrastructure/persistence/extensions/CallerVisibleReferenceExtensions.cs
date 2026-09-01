using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.infrastructure.persistence;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.extensions;

/// <summary>
/// Ownership guard for caller-supplied foreign-key ids.
///
/// <para>The per-user global query filter scopes <b>reads</b>. Copying a raw <c>long</c> out of a
/// request into an FK column is a <b>write</b>: the target row genuinely exists, so the constraint is
/// satisfied and nothing rejects it — user A can point their preset at user B's activity. Verifying
/// the referenced row through the caller's own <c>DbSet</c> is what turns another account's id back
/// into a 404, and it is an endpoint responsibility; the DbContext cannot do it.</para>
///
/// <para>Answer 404, never 403 — same reasoning as <c>MergeActivityEndpoint</c>: a 403 would confirm
/// the id exists and make another account's ids enumerable.</para>
/// </summary>
public static class CallerVisibleReferenceExtensions
{
    /// <summary>
    /// True when every non-null id names a <typeparamref name="TEntity"/> row visible to the caller.
    /// Nulls are skipped (an optional FK left unset references nothing), and an empty set is visible.
    /// </summary>
    public static async Task<bool> AllVisibleAsync<TEntity>(this DbContext dbContext, CancellationToken ct,
        params long?[] ids)
        where TEntity : class, IEntityWithId
        => await dbContext.AllVisibleAsync<TEntity>([], ct, ids);

    /// <summary>
    /// True when every non-null activity id is visible to the caller. Lifts
    /// <see cref="AppQueryFilters.ActivityArchived"/>: archiving governs which activities the
    /// <em>pickers</em> offer, and an existing row pointing at an activity the user archived later
    /// must still be editable — this check is about ownership only.
    /// </summary>
    public static Task<bool> AllActivitiesVisibleAsync(this DbContext dbContext, CancellationToken ct,
        params long?[] ids)
        => dbContext.AllVisibleAsync<Activity>([AppQueryFilters.ActivityArchived], ct, ids);

    private static async Task<bool> AllVisibleAsync<TEntity>(this DbContext dbContext, string[] ignoredQueryFilters,
        CancellationToken ct, params long?[] ids)
        where TEntity : class, IEntityWithId
    {
        var wanted = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (wanted.Length == 0)
            return true;

        var found = await dbContext.Set<TEntity>()
            .AsNoTracking()
            .IgnoreNamedQueryFilters(ignoredQueryFilters)
            .CountAsync(e => wanted.Contains(e.Id), ct);

        return found == wanted.Length;
    }
}
