using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Answers two questions about a slice Core knows nothing about: <em>which of your rows point at an
/// activity</em>, and <em>repoint them onto this other activity</em>.
/// </summary>
/// <remarks>
/// <para>
/// It exists because A9's two features are both "across every referencing entity type" and Core owns
/// only two of the twelve. <c>usageCount</c> / <c>canDelete</c> on the activity grid and
/// <c>POST /activity/merge</c> would otherwise force Core to reference History, TodoLists, Routines,
/// Planning, Tracking and ActivityProfiles — inverting the one direction the whole split rests on.
/// Same shape as <see cref="IActivityMembershipSource"/>: the owning slice implements, Core consumes,
/// neither points at the other.
/// </para>
/// <para>
/// ⚠ <see cref="ReferencingActivityIds"/> must stay a <b>composable</b> <see cref="IQueryable{T}"/>
/// built from the <see cref="DbContext"/> handed in. <c>ActivityReferenceService</c> <c>Concat</c>s
/// every source into one <c>UNION ALL</c> and correlates a <c>COUNT</c> against it inside the grid's
/// projection — that is what makes <c>usageCount</c> sortable in SQL. A <c>ToList()</c> anywhere in an
/// implementation turns one query into twelve plus a client-side group-by.
/// </para>
/// <para>
/// ⚠ <b>One row per reference, not per referencing entity.</b> A source that points at an activity
/// twice from one row (TodoLists does: <c>ActivityId</c> and <c>PairedLeisureActivityId</c>) yields
/// that row twice, because the count the UI shows is "records that would break", and the pairing is a
/// second thing that breaks. Duplicates across sources are impossible by construction.
/// </para>
/// <para>
/// ⚠ <b><see cref="RepointAsync"/> mutates the caller's context and must not call
/// <c>SaveChanges</c></b> — the same contract as <see cref="IActivityTimeAttributionSink"/>, and here
/// it is load-bearing rather than merely tidy: the merge endpoint wraps every source plus the survivor
/// delete in one transaction, and the ask makes atomicity a hard requirement. A source that saves
/// splits the merge into twelve commits, any of which can be the last one.
/// </para>
/// <para>
/// Resolved as a keyed <c>IEnumerable&lt;&gt;</c>, so — like the membership seam — a slice dropped
/// from <c>ModuleServiceExtensions.ModuleAssemblies</c> fails <b>silently</b>: the count merely comes
/// back lower and the merge quietly leaves that slice's rows pointing at a deleted activity. There is
/// no exception and no log line. <c>SeamWiringTests.ActivityReferenceSources_CoverEveryKey_Exactly</c>
/// and <c>ActivityMergeTests.Merge_RepointsEverySliceThatReferencesTheActivity</c> are the guards, and
/// the second asserts on rows in every slice rather than on the returned counts.
/// </para>
/// </remarks>
public interface IActivityReferenceSource : ISeam
{
    /// <summary>
    /// Stable key naming this source. Unlike <see cref="IActivityMembershipSource.Key"/> this one does
    /// not appear in any request payload — it is internal wiring — but it is what
    /// <c>SeamWiringTests</c> checks coverage against, so keep it in
    /// <see cref="ActivityReferenceSourceKeys"/> rather than typing the literal.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// One <c>long</c> per reference this source holds, repeating the activity id once per referencing
    /// row (see the duplicate rule in the type remarks). Composed into the caller's query as a
    /// subquery — never materialize it.
    /// </summary>
    /// <remarks>
    /// No user scoping here on purpose: <c>AppDbContext</c>'s global <c>IEntityWithUser</c> filter
    /// already scopes every entity involved by the time the subquery reaches Postgres, and the outer
    /// query is scoped too. The one entity outside that filter — <c>WebExtensionActivityEntry</c> —
    /// holds no activity FK, so no source here needs a hand-written scope.
    /// </remarks>
    IQueryable<long> ReferencingActivityIds(DbContext db);

    /// <summary>
    /// Repoints every row of this source that references one of <paramref name="mergedIds"/> onto
    /// <paramref name="survivorId"/>, and returns how many rows were changed.
    /// </summary>
    /// <remarks>
    /// <b>The implementation owns its own collision rule</b>, which is the other reason this is a seam
    /// rather than a generic "update the FK" loop: only the owning slice knows that
    /// <c>ActivityBacklogProfile.ActivityId</c> is unique (so the survivor's profile wins and the
    /// merged one is deleted), that <c>LeisureSuggestionRecord</c> is unique per
    /// (user, source, activity) (so two draw histories collapse to the more recent), and that a to-do
    /// item pointing at two merged activities through its two columns must collapse to one reference
    /// rather than fail or duplicate. A row deleted to resolve a collision counts as repointed — the
    /// number the snackbar shows is "rows that moved", and that row moved.
    /// </remarks>
    Task<int> RepointAsync(DbContext db, long survivorId, IReadOnlyCollection<long> mergedIds, CancellationToken ct);
}
