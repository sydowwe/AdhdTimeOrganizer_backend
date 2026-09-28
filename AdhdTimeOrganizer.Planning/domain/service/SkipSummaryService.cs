using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// The skip-summary rules, as a pure function over one row per eligible planner task — the range half of the
/// skip-reason insight, rendered beside the plan-vs-actual trend. The reader supplies rows and decides
/// <i>which</i> rows; this owns the grouping and the counting; the endpoint ships the result — the same split as
/// <see cref="PlanVsActualTrendService"/>.
/// <para>
/// <b>A different population from the trend, on purpose.</b> The trend drops <see cref="PlannerTaskStatus.Cancelled"/>
/// before it sees a row; this needs every planned task, because the per-hour base rate asks "of what you planned
/// at 21:00, how much was skipped", and everything planned was planned. That is why the two are sibling
/// endpoints rather than one response carrying both.
/// </para>
/// <para>
/// <b>Derived, never stored.</b> An un-skip weeks later simply leaves the next read; there is no repair path.
/// </para>
/// </summary>
public static class SkipSummaryService
{
    /// <summary>
    /// One eligible planner task, reduced to what the rules read. Eligibility (non-background, on a day that is
    /// materially over) is decided by the caller — every status arrives here, <see cref="PlannerTaskStatus.Cancelled"/>
    /// included.
    /// </summary>
    /// <param name="PlannedStart">The planned start, from <see cref="BasePlannerTask.StartTime"/>, as stored.</param>
    /// <param name="Status">The task's status.</param>
    /// <param name="SkipReason">The stored skip reason — already trimmed, blank already null (<see cref="PlannerTask.ApplyStatus"/>).</param>
    /// <param name="SkipReasonKey">The stored built-in key. At most one of this and <paramref name="SkipReason"/> is set.</param>
    public readonly record struct SkipSummaryTaskRow(
        TimeOnly PlannedStart,
        PlannerTaskStatus Status,
        string? SkipReason,
        SkipReasonKey? SkipReasonKey = null);

    /// <summary>
    /// One reason group: a built-in key (<see cref="Reason"/> null), a free-text value (<see cref="ReasonKey"/>
    /// null), or both null — "skipped with no reason", its own group, counted so totals add up.
    /// </summary>
    public readonly record struct ReasonCount(string? Reason, SkipReasonKey? ReasonKey, int Count);

    /// <summary>One hour of the day that had at least one planned task.</summary>
    public readonly record struct HourCount(int Hour, int PlannedCount, int SkippedCount);

    /// <summary>The aggregate. <see cref="SkippedCount"/> 0 means "say nothing".</summary>
    public sealed record SkipSummary(int SkippedCount, IReadOnlyList<ReasonCount> Reasons, IReadOnlyList<HourCount> ByStartHour)
    {
        public static readonly SkipSummary Empty = new(0, [], []);
    }

    /// <summary>
    /// Reduce eligible tasks to the summary. Four product decisions are encoded here:
    /// <list type="number">
    /// <item><b>A skip is <see cref="PlannerTaskStatus.Cancelled"/></b>, and nothing else — an untouched task on a
    /// day that is over was not skipped, and the trend already counts it as never happened.</item>
    /// <item><b>Reasons group by key when there is one, otherwise by exact stored value</b>, ordinal, with
    /// "neither" its own group. No case folding, no
    /// locale merging, no cap: the client folds once more (case, whitespace, the built-in labels in every locale),
    /// and that fold lives in one place so the single-day and range lines agree. Truncating here could drop two
    /// groups the client would have folded into the largest one.</item>
    /// <item><b>The hour is <see cref="TimeOnly.Hour"/> of the planned start</b>, as stored. A task wrapping
    /// midnight counts in the hour it starts.</item>
    /// <item><b>The per-hour denominator is every eligible task, in any status</b> — Cancelled and
    /// Completed-without-times included. Hours with nothing planned are omitted.</item>
    /// </list>
    /// </summary>
    public static SkipSummary Aggregate(IEnumerable<SkipSummaryTaskRow> rows)
    {
        var planned = new int[24];
        var skipped = new int[24];
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var keys = new Dictionary<SkipReasonKey, int>();
        var noReason = 0;
        var skippedCount = 0;

        foreach (var row in rows)
        {
            var hour = row.PlannedStart.Hour;
            planned[hour]++;

            if (row.Status != PlannerTaskStatus.Cancelled)
                continue;

            skippedCount++;
            skipped[hour]++;

            if (row.SkipReasonKey is { } key)
                keys[key] = keys.GetValueOrDefault(key) + 1;
            else if (row.SkipReason is { } reason)
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            else
                noReason++;
        }

        var reasonCounts = keys.Select(k => new ReasonCount(null, k.Key, k.Value))
            .Concat(reasons.Select(r => new ReasonCount(r.Key, null, r.Value)))
            .ToList();
        if (noReason > 0)
            reasonCounts.Add(new ReasonCount(null, null, noReason));

        var byStartHour = Enumerable.Range(0, 24)
            .Where(h => planned[h] > 0)
            .Select(h => new HourCount(h, planned[h], skipped[h]))
            .ToList();

        return new SkipSummary(skippedCount, reasonCounts, byStartHour);
    }
}
