using System.Linq.Expressions;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;

namespace AdhdTimeOrganizer.Planning.domain.service;

/// <summary>
/// The one definition of a block <b>left behind</b>: open — <c>NotStarted</c> or <c>InProgress</c> — and not a
/// background block, on one of the user's last <see cref="LookbackDays"/> days before today. A skip is a decision,
/// not something left behind, and a background block runs alongside the day rather than being an attempt at it.
/// <para>
/// Read by the day view's carry-over banner (<c>GetCarryOverCandidatesPlannerTaskEndpoint</c>) and by the host's
/// morning briefing, which counts the same set, so the number in the morning push is the number of rows the banner
/// then offers. The welcome-back pile reads <see cref="IsOpen"/> too but bounds the days by the absence instead.
/// </para>
/// </summary>
public static class PlannerTaskCarryOverRule
{
    /// <summary>How far back a block still counts as carried over. A week: past that, a task is a backlog item, not a slip.</summary>
    public const int LookbackDays = 7;

    /// <summary>Open and in the foreground — the half of the rule that does not depend on the day. Translates to SQL.</summary>
    public static Expression<Func<PlannerTask, bool>> IsOpen { get; } = t =>
        !t.IsBackground && (t.Status == PlannerTaskStatus.NotStarted || t.Status == PlannerTaskStatus.InProgress);

    /// <summary>Whether a block on <paramref name="date"/> is within the lookback from the user's <paramref name="today"/>.</summary>
    public static bool IsWithinLookback(DateOnly date, DateOnly today) =>
        date >= today.AddDays(-LookbackDays) && date < today;
}
