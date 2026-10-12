using Microsoft.EntityFrameworkCore;

namespace AdhdTimeOrganizer.Core.application.seam;

/// <summary>
/// Answers "when is each of these to-do items next planned?", and "how did its earlier attempts go?", without the
/// caller naming the slice that owns the planner.
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

    /// <summary>
    /// For each of <paramref name="todoListItemIds"/>, how its planner attempts went: every block ever linked to
    /// it, completed or not, sorted into the ways an attempt can fail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read side of FO3. The next-slot read above drops everything that already happened, so a to-do whose
    /// block was skipped or carried over four times looked brand new to the list. This keeps those attempts.
    /// </para>
    /// <para>
    /// Facts, not a verdict: what counts as "stuck", and what help to offer, is the to-do side's decision. The
    /// counts are for that rule only and must never reach a response — "pushed four times" is a score, and the
    /// feature exists to offer help instead of one.
    /// </para>
    /// <para>
    /// Items with no linked block at all are <b>absent</b> from the result.
    /// </para>
    /// </remarks>
    /// <param name="db">The ambient context. The query is composed from it.</param>
    /// <param name="userId">The authenticated caller, filtered on explicitly.</param>
    /// <param name="todoListItemIds">Items to report on. An empty collection returns an empty map.</param>
    /// <param name="today">The user's today, resolved by the caller in the user's own zone. A block that was never
    /// started counts as missed only once its whole day is behind the user.</param>
    Task<IReadOnlyDictionary<long, PlanHistory>> PlanHistoryAsync(
        DbContext db, long userId, IReadOnlyCollection<long> todoListItemIds, DateOnly today, CancellationToken ct);

    /// <summary>
    /// For each of <paramref name="todoListItemIds"/>, the length in seconds of the <b>first</b> planner block ever
    /// linked to it — the first one created, whatever happened to it since.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The planned side of F3-01's estimate fit. The first block is the user's first guess at how long the item
    /// needs in the day; a later block may only be what was left over, and a carry-over keeps the block's length,
    /// so neither tells you more. Skipped and carried blocks still count: the question is what was planned, not
    /// what happened.
    /// </para>
    /// <para>
    /// Items with no linked block are <b>absent</b> from the result.
    /// </para>
    /// </remarks>
    /// <param name="db">The ambient context. The query is composed from it.</param>
    /// <param name="userId">The authenticated caller, filtered on explicitly.</param>
    /// <param name="todoListItemIds">Items to report on. An empty collection returns an empty map.</param>
    Task<IReadOnlyDictionary<long, int>> FirstPlannedSecondsAsync(
        DbContext db, long userId, IReadOnlyCollection<long> todoListItemIds, CancellationToken ct);
}

/// <param name="Date">The planner day, on the user's clock.</param>
/// <param name="StartTime">The block's start on that day.</param>
public record PlannedSlot(DateOnly Date, TimeOnly StartTime);

/// <summary>
/// One to-do item's planner attempts, sorted by outcome. Skip reasons are spelled out field by field because
/// Core cannot see Planning's <c>SkipReasonKey</c>.
/// </summary>
/// <param name="Completed">Blocks marked completed, by the user or by a tracker.</param>
/// <param name="CarriedOver">The sum of every linked block's carry-over count: each carry is one day the item
/// did not happen on. A carried block that later fails again counts again, under its new outcome.</param>
/// <param name="Missed">Foreground blocks still <c>NotStarted</c> or <c>OnHold</c> on a day before the user's
/// today. Today's are not counted: the day is not over.</param>
/// <param name="SkippedNoTime">Skipped with the built-in "ran out of time" reason.</param>
/// <param name="SkippedNoEnergy">Skipped with the built-in "no energy" reason.</param>
/// <param name="SkippedNotRelevant">Skipped with the built-in "not relevant any more" reason.</param>
/// <param name="SkippedOther">Skipped with a free-text reason or none.</param>
/// <param name="Postponed">The sum of every linked block's postpone count: each time a block whose slot had
/// passed was pushed later instead of started. Disjoint from <paramref name="CarriedOver"/> — the planner counts a
/// carry in one and a postponement in the other, never both.</param>
public record PlanHistory(
    int Completed,
    int CarriedOver,
    int Missed,
    int SkippedNoTime,
    int SkippedNoEnergy,
    int SkippedNotRelevant,
    int SkippedOther,
    int Postponed = 0)
{
    public int FailedAttempts =>
        CarriedOver + Postponed + Missed + SkippedNoTime + SkippedNoEnergy + SkippedNotRelevant + SkippedOther;
}
