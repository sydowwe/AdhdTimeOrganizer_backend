using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Answers the one question a write path has to ask about an id it did not mint: <em>does this row of
/// yours belong to this user?</em>
/// </summary>
/// <remarks>
/// <para>
/// A request DTO carrying a bare <c>long</c> for another slice's row is unguarded by construction: the
/// database enforces existence, never ownership, and the row the caller is creating is stamped with
/// the caller's own <c>UserId</c>, so nothing downstream can tell the link apart from a legitimate
/// one. <c>ActivityHistory.TodoListItemId</c> / <c>.RoutineTodoListId</c> are the first two — History
/// cannot see the slices that own those rows, which is exactly why the check has to come back through
/// Core.
/// </para>
/// <para>
/// Ownership is <b>not</b> the same question as the ambient query filter, even though both come down
/// to <c>UserId</c>. Implementations filter on the <paramref name="userId"/> they are handed and never
/// lean on the filter alone — the filter answers "who is executing", and a verifier that agreed with
/// it by accident would pass in every background or unauthenticated context.
/// </para>
/// <para>
/// Resolved as a keyed <c>IEnumerable&lt;&gt;</c> like <see cref="IActivityMembershipSource"/>, since
/// two slices implement it. Unlike that one, the consumer
/// (<c>IReferencedIdOwnershipService</c>) <b>fails closed</b> when no verifier claims the key: a
/// missing implementation rejects the write rather than waving it through. That is the safe direction
/// but still a silent behaviour change, which is why
/// <c>SeamWiringTests.UserOwnedReferenceVerifiers_CoverEveryKey_Exactly</c> pins the coverage.
/// </para>
/// </remarks>
public interface IUserOwnedReferenceVerifier : ISeam
{
    /// <summary>
    /// Which id this verifier speaks for. Internal wiring only — unlike
    /// <see cref="IActivityMembershipSource.Key"/> this never reaches the wire, so it can be renamed
    /// with its constant.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// Whether a row with <paramref name="id"/> exists in this verifier's table and belongs to
    /// <paramref name="userId"/>. A row that does not exist and one owned by somebody else are the
    /// same answer on purpose — the caller turns both into "not found", which is what stops the check
    /// itself confirming that a stranger's id is real.
    /// </summary>
    /// <param name="db">The ambient context. The query is composed from it.</param>
    /// <param name="userId">The authenticated caller, filtered on explicitly.</param>
    /// <param name="id">The client-supplied id to check.</param>
    Task<bool> ExistsForUserAsync(DbContext db, long userId, long id, CancellationToken ct);
}
