using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// FO4: the one definition of a <b>missed</b> planner block — <c>NotStarted</c>, not a background block, and its
/// end has passed <b>on the user's own clock</b>. Derived and never stored, so un-missing a block (starting it,
/// moving it later) needs no write anywhere else. The day's re-plan reads it here; audit N5's "you missed …" nudge
/// is meant to read it here too rather than writing a second copy.
/// <para>
/// Background blocks are never missed: they run alongside the day rather than being an attempt at it — the same
/// reading <c>GetCarryOverCandidatesPlannerTaskEndpoint</c> and the to-do plan history take. A block the user
/// began (<c>InProgress</c>, <c>OnHold</c>) or closed (<c>Completed</c>, <c>Cancelled</c>) is not missed either:
/// something was decided about it.
/// </para>
/// <para>
/// The to-do plan history (<c>TodoListItemPlanSource.PlanHistoryAsync</c>) counts a block as missed on a whole
/// day granularity — any open foreground block on a day before the user's today, <c>OnHold</c> included. That is
/// this rule with the whole day behind the user, plus a paused block nobody came back to; it is folded in SQL there
/// and deliberately not rewritten onto this one.
/// </para>
/// </summary>
public static class PlannerTaskMissedRule
{
    /// <summary>Whether the block is missed at <paramref name="userNow"/>, the user's wall clock.</summary>
    public static bool IsMissed(
        PlannerTaskStatus status, bool isBackground, DateOnly date, TimeOnly startTime, TimeOnly endTime, DateTime userNow) =>
        Classify(status, isBackground, date, startTime, endTime, userNow) == PlannerTaskSlipState.Missed;

    /// <inheritdoc cref="IsMissed(PlannerTaskStatus,bool,DateOnly,TimeOnly,TimeOnly,DateTime)"/>
    /// <param name="task">The task.</param>
    /// <param name="date">The task's day (<c>Calendar.Date</c>) — passed in, so the calendar need not be loaded.</param>
    /// <param name="userNow">The current wall-clock time in the user's own zone.</param>
    public static bool IsMissed(PlannerTask task, DateOnly date, DateTime userNow) =>
        IsMissed(task.Status, task.IsBackground, date, task.StartTime, task.EndTime, userNow);

    /// <summary>
    /// Missed, late or upcoming — or <c>null</c> when the rule does not apply at all: the block was begun or closed,
    /// or runs in the background.
    /// <para>
    /// <b>Late</b> is a block whose start has passed (strictly) while its end has not: it is slipping, not yet
    /// missed. A block exactly at its start minute is still upcoming, so a re-plan asked for at 11:00 does not treat
    /// the 11:00 block as already behind.
    /// </para>
    /// </summary>
    public static PlannerTaskSlipState? Classify(
        PlannerTaskStatus status, bool isBackground, DateOnly date, TimeOnly startTime, TimeOnly endTime, DateTime userNow)
    {
        if (status != PlannerTaskStatus.NotStarted || isBackground)
            return null;

        if (EndsAt(date, startTime, endTime) <= userNow)
            return PlannerTaskSlipState.Missed;

        return date.ToDateTime(startTime) < userNow ? PlannerTaskSlipState.Late : PlannerTaskSlipState.Upcoming;
    }

    /// <summary>
    /// The wall-clock moment the block's slot ends. An end <b>before</b> the start wrapped past midnight onto the
    /// next day — the convention <c>CompleteFinishedPlannerTasksJobHandler</c> follows for the rows that predate
    /// <c>PlannerTaskValidator</c>'s ordering rule. An end equal to the start is an empty slot, not a 24-hour one.
    /// </summary>
    public static DateTime EndsAt(DateOnly date, TimeOnly startTime, TimeOnly endTime) =>
        (endTime < startTime ? date.AddDays(1) : date).ToDateTime(endTime);
}
