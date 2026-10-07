using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// FO3 phase 2: the one rule that decides whether moving a planner task was a <b>postponement</b>, and records it
/// on the task. Every write path that moves a task's day or start time goes through
/// <c>PlannerTaskMoveRecorder</c>, which calls this: the PUT (home's <i>Move later</i>, the day view's reschedule),
/// the span patch (a drag) and FO4's re-plan apply (<c>ApplyReplanCalendarEndpoint</c>), for every block it moves
/// within the day or to tomorrow. A new path that moves a task must call it too, or its moves leave no trace.
/// <para>
/// <b>A move is a postponement</b> when all three hold:
/// <list type="number">
/// <item>the task was <c>NotStarted</c> before the write. Moving a running or paused block is re-arranging work
/// already begun, and a closed one has nothing left to put off;</item>
/// <item>the new slot is later than the old one, by day or by time;</item>
/// <item>the task's <b>original</b> slot has passed on the user's clock. Before its first slip that is the slot it
/// is moving from; after it, the slot it was first planned for (<see cref="PlannerTask.OriginalDate"/> +
/// <see cref="PlannerTask.OriginalStartTime"/>). So an early tidy-up of tomorrow is a re-plan, and once a task
/// has slipped, every further push counts, even one made hours before the new slot.</item>
/// </list>
/// </para>
/// <para>
/// A postponement adds one to <see cref="PlannerTask.PostponeCount"/> and, the first time, fixes the original
/// slot. The carry-over command keeps its own count (<see cref="PlannerTask.CarryOverCount"/>) and does not count
/// here, so the two never count one slip twice.
/// </para>
/// </summary>
public static class PlannerTaskPostponeRule
{
    /// <summary>
    /// Records the move from (<paramref name="fromDate"/>, <paramref name="fromStart"/>) to
    /// (<paramref name="toDate"/>, <paramref name="toStart"/>) if it was a postponement.
    /// </summary>
    /// <param name="task">The task, already carrying its new slot or not — only its original-slot fields and
    /// counter are read and written.</param>
    /// <param name="statusBefore">The status the task had before this write.</param>
    /// <param name="userNow">The current wall-clock time in the user's own zone.</param>
    /// <returns>Whether the move was counted.</returns>
    public static bool Apply(
        PlannerTask task,
        PlannerTaskStatus statusBefore,
        DateOnly fromDate,
        TimeOnly fromStart,
        DateOnly toDate,
        TimeOnly toStart,
        DateTime userNow)
    {
        if (statusBefore != PlannerTaskStatus.NotStarted)
            return false;

        if (toDate.ToDateTime(toStart) <= fromDate.ToDateTime(fromStart))
            return false;

        // A task carried over before OriginalStartTime existed has a date and no time; its time of day was
        // kept by the carry, so the current start stands in for it.
        var originalSlot = (task.OriginalDate ?? fromDate).ToDateTime(task.OriginalStartTime ?? fromStart);
        if (originalSlot > userNow)
            return false;

        task.PostponeCount++;
        // Set as a pair, on the first slip only — the same rule the carry-over command follows, so a row carried
        // before OriginalStartTime existed keeps reading its current start rather than pairing its old date with
        // whatever time it has been moved to since.
        if (task.OriginalDate is null)
        {
            task.OriginalDate = fromDate;
            task.OriginalStartTime = fromStart;
        }
        return true;
    }

    /// <summary>
    /// The start a block's drift is measured from: its original start while it is still on the day it was first
    /// planned for, and its current start otherwise.
    /// <para>
    /// Snoozing a 10:00 block to 14:00 and starting it at 14:00 is four hours late, not on time — measuring from
    /// the snoozed start made the trend understate exactly the drift it exists to show. A block carried to a
    /// later day is a fresh attempt instead (its actual times are cleared by the carry), and yesterday's time of
    /// day says nothing about when today's attempt should have begun.
    /// </para>
    /// </summary>
    public static TimeOnly DriftAnchor(DateOnly date, TimeOnly startTime, DateOnly? originalDate, TimeOnly? originalStartTime) =>
        originalDate == date && originalStartTime is { } original ? original : startTime;
}
