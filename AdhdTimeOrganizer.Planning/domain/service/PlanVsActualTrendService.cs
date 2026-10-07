using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// The plan-vs-actual drift rules, as a pure function over one row per eligible planner task. Every product
/// decision the trend makes lives here; the reader supplies rows and decides <i>which</i> rows, the endpoint
/// ships the result — the same split as <see cref="PlannerStreakService"/>.
/// <para>
/// <b>Derived, never stored.</b> Nothing here is materialised: the six numbers are recomputed from
/// <see cref="PlannerTask"/> rows on every read, so editing a task weeks back moves the trend with no repair
/// path to write. That is affordable because the range is capped and the rows are narrow — see
/// <c>GetPlanVsActualTrendPlannerTaskEndpoint</c>.
/// </para>
/// <para>
/// <b>Two sums over two different populations.</b> <see cref="PlanVsActualTrend.PlannedMinutes"/> covers every
/// eligible task; <see cref="PlanVsActualTrend.ActualMinutes"/> covers only the ones that were completed
/// <i>and</i> carry both actual times. They are deliberately not comparable as a ratio without
/// <see cref="PlanVsActualTrend.NeverHappenedCount"/> in view, which is exactly why that count is in the
/// response rather than left for the client to infer.
/// </para>
/// </summary>
public static class PlanVsActualTrendService
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>Half a day — the split point that decides whether a time-of-day difference wrapped midnight.</summary>
    private const int HalfDayMinutes = MinutesPerDay / 2;

    /// <summary>
    /// One eligible planner task, reduced to the five fields the rules read. Eligibility (non-background, not
    /// <see cref="PlannerTaskStatus.Cancelled"/>, on a day that is materially over) is decided by the caller —
    /// every row handed here is already counted in <see cref="PlanVsActualTrend.TaskCount"/>.
    /// </summary>
    /// <param name="PlannedStart">The planned start, from <see cref="BasePlannerTask.StartTime"/>.</param>
    /// <param name="PlannedEnd">The planned end. May be before <paramref name="PlannedStart"/> — a task that wraps midnight.</param>
    /// <param name="Status">The task's status. Never <see cref="PlannerTaskStatus.Cancelled"/>.</param>
    /// <param name="ActualStart">When it actually started, if it was recorded.</param>
    /// <param name="ActualEnd">When it actually ended, if it was recorded.</param>
    /// <param name="DriftFrom">The start lateness is measured from, when it is not <paramref name="PlannedStart"/>:
    /// a block postponed within its day is late against the start it was first planned for
    /// (<see cref="PlannerTaskPostponeRule.DriftAnchor"/>). The span is still the planned one.</param>
    public readonly record struct PlanVsActualTaskRow(
        TimeOnly PlannedStart,
        TimeOnly PlannedEnd,
        PlannerTaskStatus Status,
        TimeOnly? ActualStart,
        TimeOnly? ActualEnd,
        TimeOnly? DriftFrom = null);

    /// <summary>The six numbers the trend surface renders. All non-negative; all zero when nothing is eligible.</summary>
    public readonly record struct PlanVsActualTrend(
        int TaskCount,
        int PlannedMinutes,
        int ActualMinutes,
        int StartedLateCount,
        int RanLongerCount,
        int NeverHappenedCount);

    /// <summary>
    /// Reduce eligible tasks to the trend. Five product decisions are encoded here:
    /// <list type="number">
    /// <item><b>Planned time counts for every eligible task</b>, whether or not it happened. A day's plan is
    /// what the person committed to, so a task that never started still spent budget in the plan — dropping it
    /// from <see cref="PlanVsActualTrend.PlannedMinutes"/> would make an unfinished range look perfectly
    /// estimated.</item>
    /// <item><b>Actual time counts only for completed tasks carrying both actual times.</b> A completed task
    /// missing either time contributes nothing to <see cref="PlanVsActualTrend.ActualMinutes"/> and is not a
    /// "never happened" either — it is simply unmeasurable, and guessing one end from the plan would fabricate
    /// exactly the drift the number exists to measure.</item>
    /// <item><b>Late and long are counted over the measured population only</b>, never over
    /// <see cref="PlanVsActualTrend.TaskCount"/>. A task with no actual times cannot be late or long, and a
    /// task that never happened is counted once, in <see cref="PlanVsActualTrend.NeverHappenedCount"/>.</item>
    /// <item><b>Both comparisons are made on the whole minutes the response reports</b>, not on the raw
    /// values. A task that ran thirty seconds past its plan is not "longer" in any sense the reader can see on
    /// screen, and counting it would make <see cref="PlanVsActualTrend.RanLongerCount"/> disagree with the two
    /// sums beside it.</item>
    /// <item><b>Everything is midnight-wrap aware.</b> A task planned 23:00–00:30 is 90 minutes, not a
    /// negative span — see <see cref="SpanMinutes"/> and <see cref="DriftMinutes"/>.</item>
    /// </list>
    /// </summary>
    public static PlanVsActualTrend Aggregate(IEnumerable<PlanVsActualTaskRow> rows)
    {
        var taskCount = 0;
        var plannedMinutes = 0;
        var actualMinutes = 0;
        var startedLate = 0;
        var ranLonger = 0;
        var neverHappened = 0;

        foreach (var row in rows)
        {
            taskCount++;

            var planned = SpanMinutes(row.PlannedStart, row.PlannedEnd);
            plannedMinutes += planned;

            if (row.Status != PlannerTaskStatus.Completed)
            {
                // The day is over and the task never reached Completed. Cancelled never gets here — a skip left
                // the population upstream, because the app presents skipping as a legitimate way to close a
                // task and counting it as a failure would contradict the UI silently.
                neverHappened++;
                continue;
            }

            if (row.ActualStart is not { } actualStart || row.ActualEnd is not { } actualEnd)
                continue;

            var actual = SpanMinutes(actualStart, actualEnd);
            actualMinutes += actual;

            if (DriftMinutes(row.DriftFrom ?? row.PlannedStart, actualStart) > 0)
                startedLate++;

            if (actual > planned)
                ranLonger++;
        }

        return new PlanVsActualTrend(taskCount, plannedMinutes, actualMinutes, startedLate, ranLonger, neverHappened);
    }

    /// <summary>
    /// Whole minutes from <paramref name="start"/> to <paramref name="end"/>, going forward through midnight if
    /// it has to. <c>TimeOnly</c> subtraction already wraps, so a 23:00–00:30 task reads 90 rather than -1350.
    /// <para>
    /// A task whose end equals its start reads <b>0</b>, not a full day. Both readings are defensible for a
    /// degenerate span, and zero is the one that cannot silently add 24 hours to a range's planned total.
    /// </para>
    /// </summary>
    internal static int SpanMinutes(TimeOnly start, TimeOnly end) =>
        (int)Math.Round((end - start).TotalMinutes, MidpointRounding.AwayFromZero);

    /// <summary>
    /// How many minutes <paramref name="actual"/> falls after <paramref name="planned"/> — positive for late,
    /// negative for early, zero for on time.
    /// <para>
    /// Times of day carry no date, so the raw difference is only ever a position on a 24-hour ring: a task
    /// planned for 23:50 and started at 00:10 is twenty minutes late, not twenty-three hours and forty minutes
    /// early. This resolves that by taking the <b>shorter way round</b> — a difference of more than twelve
    /// hours is read as the other direction. The ambiguity is inherent and cannot be removed here; a task
    /// genuinely started thirteen hours late is indistinguishable from one started eleven hours early, and the
    /// near reading is chosen because it is overwhelmingly the likelier one.
    /// </para>
    /// </summary>
    internal static int DriftMinutes(TimeOnly planned, TimeOnly actual)
    {
        // TimeOnly subtraction is already normalised into [0, 24h), so this needs no modulo of its own.
        var forward = (int)Math.Round((actual - planned).TotalMinutes, MidpointRounding.AwayFromZero);
        return forward > HalfDayMinutes ? forward - MinutesPerDay : forward;
    }
}
