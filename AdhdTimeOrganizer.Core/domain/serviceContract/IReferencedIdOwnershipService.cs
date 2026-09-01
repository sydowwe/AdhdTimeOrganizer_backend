using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.domain.serviceContract;

/// <summary>
/// "This write carries an id the caller typed — is it theirs?" The one face endpoints use for the
/// ownership half of request validation, over Core's own <c>Activity</c> and over the
/// <c>IUserOwnedReferenceVerifier</c> seam for ids another slice owns.
/// </summary>
/// <remarks>
/// <para>
/// The rule this exists to enforce: <b>a client-supplied id is proof of nothing.</b> An FK enforces
/// existence, not ownership, and the row being written is stamped with the caller's own
/// <c>UserId</c> — so a request naming a stranger's <c>ActivityId</c> inserts cleanly and the global
/// query filter then hides the other end of the join on every subsequent read. The write path is the
/// only layer holding a principal, so it is the only layer that can check.
/// </para>
/// <para>
/// Declared as an interface rather than used as a concrete class for the same reason as
/// <see cref="IActivityReferenceService"/>: the Scrutor scan registers <c>AsImplementedInterfaces()</c>,
/// so a class carrying only <c>IScopedService</c> does not resolve as itself. Resolved as a single
/// service, so a missing registration throws at endpoint activation instead of quietly skipping the
/// check.
/// </para>
/// </remarks>
public interface IReferencedIdOwnershipService
{
    /// <summary>
    /// Whether <paramref name="activityId"/> is an activity owned by <paramref name="userId"/>.
    /// </summary>
    /// <remarks>
    /// Archived activities count as owned: <c>AppQueryFilters.ActivityArchived</c> is lifted here.
    /// Archival hides an activity from the pickers, it does not make a recording that references it
    /// invalid, and letting the flag decide would turn "you archived this activity" into a 404 on an
    /// edit of history that predates the archiving. The per-user filter is left on, and the
    /// <c>UserId</c> is compared explicitly regardless.
    /// </remarks>
    Task<bool> OwnsActivityAsync(DbContext db, long userId, long activityId, CancellationToken ct);

    /// <summary>
    /// Whether <paramref name="id"/> is a row of the table named by <paramref name="key"/> (a
    /// <c>UserOwnedReferenceKeys</c> constant) owned by <paramref name="userId"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Fails closed.</b> A key no <c>IUserOwnedReferenceVerifier</c> claims answers <c>false</c>,
    /// so a slice dropped from <c>ModuleServiceExtensions.ModuleAssemblies</c> rejects every write
    /// carrying that id rather than accepting them all unchecked. Guarded by
    /// <c>SeamWiringTests.UserOwnedReferenceVerifiers_CoverEveryKey_Exactly</c>.
    /// </remarks>
    Task<bool> OwnsReferenceAsync(DbContext db, string key, long userId, long id, CancellationToken ct);
}
