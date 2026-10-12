using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Answers "which recordings were logged against each of these routine items, and when?" without the caller naming
/// the slice that owns the time ledger. The routine twin of <see cref="ITodoListItemLoggedTimeSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Routines learns how long each item usually takes (F2-06), and that time lives in <c>ActivityHistory</c>, which
/// belongs to History. History implements this; Routines depends on Core alone.
/// </para>
/// <para>
/// Keyed on <c>ActivityHistory.RoutineTodoListId</c> <b>only</b>, stamped when the user logs time or runs a timer from
/// the routine item. Widening it to "any time logged against this item's activity" looks strictly more generous and
/// is wrong: two routine items (in two periods) may share one activity, and each would be credited the same seconds.
/// Rows logged before the client started sending the link (2026-10-03) are untagged and correctly contribute nothing.
/// </para>
/// <para>
/// It returns the recordings rather than a sum, unlike its to-do twin, because the caller buckets them into the
/// routine's own cycles — and those are the Routines slice's to define, not History's.
/// </para>
/// <para>
/// Resolved as a <em>single</em> service: a missing registration throws at endpoint activation rather than silently
/// reporting that no routine item has ever been timed.
/// </para>
/// </remarks>
public interface IRoutineItemLoggedTimeSource : ISeam
{
    /// <summary>
    /// Every recording linked to one of <paramref name="routineTodoListIds"/> that started in
    /// <c>[fromUtc, toUtc)</c>, one entry per row. Unordered.
    /// </summary>
    /// <param name="db">The ambient context. The query is composed from it.</param>
    /// <param name="userId">
    /// The authenticated caller. Filtered on explicitly, never left to the ambient query filter.
    /// </param>
    /// <param name="routineTodoListIds">Items to report on. An empty collection returns an empty list.</param>
    /// <param name="fromUtc">Inclusive lower bound on the recording's start, as a UTC instant.</param>
    /// <param name="toUtc">Exclusive upper bound on the recording's start, as a UTC instant.</param>
    Task<IReadOnlyList<RoutineItemLoggedTime>> LoggedTimeBetweenAsync(
        DbContext db, long userId, IReadOnlyCollection<long> routineTodoListIds, DateTime fromUtc, DateTime toUtc,
        CancellationToken ct);
}

/// <summary>One recording logged against a routine item: which item, when it started (UTC) and how long it ran.</summary>
public readonly record struct RoutineItemLoggedTime(long RoutineTodoListId, DateTime StartUtc, long Seconds);
