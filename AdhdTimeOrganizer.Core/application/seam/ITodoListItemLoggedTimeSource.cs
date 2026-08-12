using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Answers "how many seconds were logged against each of these to-do items on this day?" without the
/// caller naming the slice that owns the time ledger.
/// </summary>
/// <remarks>
/// <para>
/// TodoLists' daily recap needs logged time per item, and that time lives in <c>ActivityHistory</c>,
/// which belongs to History. Reading it directly is what would force a TodoLists → History project
/// reference. History implements this; TodoLists depends on Core alone.
/// </para>
/// <para>
/// The attribution is exact rather than inferred. <c>ActivityHistory</c> carries a nullable
/// <c>TodoListItemId</c>, stamped when the user answers the save-to-history prompt on completion, and
/// implementations must key off <b>that column only</b>. Falling back to "any time logged against
/// this item's activity" looks like a harmless improvement and is not: two to-do items may share one
/// activity, so the same seconds would be credited to both and the recap's rows would sum to more
/// time than the day contains. Rows predating the column are untagged and correctly contribute zero.
/// </para>
/// <para>
/// Resolved as a <em>single</em> service, like <see cref="IActivityTimeAttributionSink"/> and unlike
/// the keyed <see cref="IActivityMembershipSource"/>: a missing registration throws at endpoint
/// activation rather than silently reporting a zero day for every item.
/// </para>
/// </remarks>
public interface ITodoListItemLoggedTimeSource : ISeam
{
    /// <summary>
    /// Seconds logged against each of <paramref name="todoListItemIds"/> on <paramref name="day"/>.
    /// </summary>
    /// <remarks>
    /// Items with no logged time on that day are <b>absent</b> from the result rather than present
    /// with a zero — the caller is listing completed items and supplies its own zero, so returning a
    /// key per requested id would only invite a lookup that cannot fail and hide the ones that did.
    /// </remarks>
    /// <param name="db">The ambient context. The query is composed from it.</param>
    /// <param name="userId">
    /// The authenticated caller. Filtered on explicitly, never left to the ambient query filter — the
    /// caller passes the id it authenticated and this must not attribute to whoever is ambient.
    /// </param>
    /// <param name="todoListItemIds">Items to report on. An empty collection returns an empty map.</param>
    /// <param name="day">The day to scope to, interpreted as a half-open UTC range.</param>
    Task<IReadOnlyDictionary<long, long>> LoggedSecondsOnDayAsync(
        DbContext db, long userId, IReadOnlyCollection<long> todoListItemIds, DateOnly day, CancellationToken ct);
}
