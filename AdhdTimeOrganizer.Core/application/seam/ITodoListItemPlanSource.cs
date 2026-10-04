using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Answers "when is each of these to-do items next planned?" without the caller naming the slice that owns the
/// planner.
/// </summary>
/// <remarks>
/// <para>
/// A to-do item's <c>DueDate</c> is a <b>deadline</b>; a planner block linked to it is <b>when it is scheduled</b>.
/// They used to share the one field: planning a block from a to-do stamped the block's date into an empty
/// <c>DueDate</c>, so a missed block turned the to-do overdue and a moved one left its time stale. Now the deadline
/// stays the user's, and "is this scheduled?" is read from the planner through this seam. Planning implements it;
/// TodoLists depends on Core alone — the same shape as <see cref="ITodoListItemLoggedTimeSource"/>, the other way.
/// </para>
/// <para>
/// Resolved as a <em>single</em> service: a missing registration throws at endpoint activation rather than
/// silently reporting every item as unscheduled.
/// </para>
/// </remarks>
public interface ITodoListItemPlanSource : ISeam
{
    /// <summary>
    /// For each of <paramref name="todoListItemIds"/>, its earliest open planner block (not completed, not skipped)
    /// on or after <paramref name="fromDate"/>, ordered by date then start time.
    /// </summary>
    /// <remarks>
    /// Items with no such block are <b>absent</b> from the result. A block from before <paramref name="fromDate"/>
    /// that was never done does not count: the item slipped, and saying it is still scheduled would hide that.
    /// </remarks>
    /// <param name="db">The ambient context. The query is composed from it.</param>
    /// <param name="userId">The authenticated caller, filtered on explicitly.</param>
    /// <param name="todoListItemIds">Items to report on. An empty collection returns an empty map.</param>
    /// <param name="fromDate">The user's today, resolved by the caller in the user's own zone.</param>
    Task<IReadOnlyDictionary<long, PlannedSlot>> NextPlannedSlotsAsync(
        DbContext db, long userId, IReadOnlyCollection<long> todoListItemIds, DateOnly fromDate, CancellationToken ct);
}

/// <param name="Date">The planner day, on the user's clock.</param>
/// <param name="StartTime">The block's start on that day.</param>
public record PlannedSlot(DateOnly Date, TimeOnly StartTime);
