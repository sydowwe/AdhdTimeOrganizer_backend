using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Answers one question about a slice that Core knows nothing about: <em>which activities appear in
/// your area, optionally narrowed by one facet of it?</em>
/// </summary>
/// <remarks>
/// <para>
/// This exists so a slice can filter on "activity is on a to-do list" / "activity is in a routine"
/// without referencing the slice that owns to-do lists or routines. History is the first consumer:
/// its grid used to reach into <c>TodoListItems</c> / <c>RoutineTodoLists</c> directly, which made
/// History depend on TodoLists <i>and</i> Routines and forced all three to be extracted in a fixed
/// order. With this seam History depends on Core alone.
/// </para>
/// <para>
/// The direction matters: the owning slice implements this and points at Core; the consuming slice
/// consumes it and points at Core. Neither points at the other, so no edge is created in either
/// direction. Implementations carry a lifetime marker (<c>IScopedService</c>) and are resolved as
/// <c>IEnumerable&lt;IActivityMembershipSource&gt;</c> — the DI scan registers by implemented
/// interface, so every implementation in every scanned assembly shows up.
/// </para>
/// <para>
/// ⚠ <see cref="ActivityIds"/> must stay a <b>composable</b> <see cref="IQueryable{T}"/> built from
/// the <see cref="DbContext"/> handed in. Callers use it as a subquery
/// (<c>ids.Contains(x.ActivityId)</c>, which EF renders as <c>IN (SELECT …)</c>); materializing it
/// with <c>ToList()</c> would pull every id into memory and turn one <c>EXISTS</c> into a two-round-
/// trip client-side filter.
/// </para>
/// </remarks>
public interface IActivityMembershipSource
{
    /// <summary>
    /// Stable wire key naming this source. It appears in filter DTOs and therefore in the SPA's
    /// request payloads — treat it as public API and do not rename it with the implementing class.
    /// </summary>
    string Key { get; }

    /// <summary>
    /// The activity ids belonging to this source, optionally narrowed by a source-specific facet
    /// (a task priority for to-do lists, a time period for routines). Duplicates are fine — callers
    /// only ever ask for membership.
    /// </summary>
    /// <param name="db">The ambient context. The returned query must be composed from it.</param>
    /// <param name="facetId">Source-specific narrowing; <c>null</c> means "any".</param>
    IQueryable<long> ActivityIds(DbContext db, long? facetId);
}
