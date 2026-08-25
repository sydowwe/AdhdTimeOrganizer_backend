namespace AdhdTimeOrganizer.Planning.application.dto.response.taskPlanner;

/// <summary>
/// How a person's planned time compared with their logged time over a date range — the cross-day counterpart
/// of the single-day plan-vs-actual block, which the client computes for itself from the day it already has
/// on screen.
/// <para>
/// <b><see cref="TaskCount"/> of 0 means "say nothing", not "everything was zero".</b> A range with no
/// eligible tasks — nothing planned, everything skipped, or the whole range still in progress or in the
/// future — comes back as six zeroes, and the client renders nothing rather than a zeroed-out card. There is
/// no separate "no data" flag to read.
/// </para>
/// <para>
/// <b>The two sums do not cover the same tasks.</b> <see cref="PlannedMinutes"/> spans every task in
/// <see cref="TaskCount"/>; <see cref="ActualMinutes"/> spans only the completed ones that carry both actual
/// times. Reading <c>actual / planned</c> alone therefore understates the person's pace whenever
/// <see cref="NeverHappenedCount"/> is non-zero — that count belongs in whatever sentence the ratio appears
/// in.
/// </para>
/// </summary>
public record PlanVsActualTrendResponse
{
    /// <summary>
    /// Tasks the aggregate is built from: not background, not cancelled, and falling on a day in the range
    /// that is materially over. Cancelled tasks leave the population entirely — they are not counted here and
    /// not counted in <see cref="NeverHappenedCount"/>.
    /// </summary>
    public required int TaskCount { get; init; }

    /// <summary>
    /// Planned minutes summed over every task in <see cref="TaskCount"/>, midnight-wrap aware. Includes tasks
    /// that never happened — the plan is what the person committed to, whether or not they got to it.
    /// </summary>
    public required int PlannedMinutes { get; init; }

    /// <summary>
    /// Logged minutes summed over the tasks that are completed <b>and</b> carry both actual times. A completed
    /// task missing either time is excluded from this sum — and is not a "never happened" either.
    /// </summary>
    public required int ActualMinutes { get; init; }

    /// <summary>Of the tasks counted in <see cref="ActualMinutes"/>, how many started later than planned.</summary>
    public required int StartedLateCount { get; init; }

    /// <summary>Of the tasks counted in <see cref="ActualMinutes"/>, how many ran longer than planned.</summary>
    public required int RanLongerCount { get; init; }

    /// <summary>
    /// Tasks in <see cref="TaskCount"/> that never reached completion on a day that is over — still
    /// not started, in progress, or on hold.
    /// </summary>
    public required int NeverHappenedCount { get; init; }
}
