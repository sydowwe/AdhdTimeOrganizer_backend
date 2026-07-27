using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.@enum;

namespace AdhdTimeOrganizer.Scheduler.application.dashboard;

/// <summary>
/// The stuck/overdue signal shared by the jobs-overview filter, the health view and the active overdue sweep:
/// an <c>Active</c> job whose <c>NextRunAt</c> is in the past by more than a margin has missed a fire. The
/// margin avoids a false positive in the brief window between a fire and the dispatcher writing the new
/// <c>NextRunAt</c>; the default equals the Quartz misfire threshold (Scheduler leaves the Quartz default of
/// 60s — see <c>SchedulerQuartzConfig</c>).
/// <para>
/// <b>One predicate, a caller-chosen margin.</b> <see cref="WhereOverdue"/> is the module's single definition
/// of "overdue" — the pull views and the push sweep (follow-up 08) must never grow a second one. Only the
/// margin is parameterized, because a <i>display</i> margin and an <i>alert</i> margin want different values:
/// 60s is right for a dashboard column (show the operator everything that looks late), and far too eager for a
/// notification (a job whose handler simply runs longer than 60s would still be holding its stale
/// <c>NextRunAt</c> and would be paged as "never fired"). See <c>OverdueJobSweepOptions.AlertMarginMinutes</c>.
/// </para>
/// <para>
/// <b>Paused / Removed jobs are excluded by construction</b> — twice over. The <c>Status == Active</c> clause
/// filters them, and <c>SchedulerService.PauseJobAsync</c> / <c>RemoveJobAsync</c> also null out
/// <c>NextRunAt</c>, which the <c>NextRunAt != null</c> clause then rejects. A job with no <c>NextRunAt</c> has
/// no fire expectation at all and can therefore never be "late".
/// </para>
/// </summary>
public static class OverduePolicy
{
    /// <summary>Default margin == the Quartz default misfire threshold (60s). A 1s-late job is not yet "stuck".</summary>
    public static readonly TimeSpan GraceMargin = TimeSpan.FromSeconds(60);

    /// <summary>The <c>NextRunAt</c> cutoff for "overdue", computed from <paramref name="utcNow"/>.</summary>
    public static DateTime Threshold(DateTime utcNow, TimeSpan? margin = null) => utcNow - (margin ?? GraceMargin);

    /// <summary>
    /// Restricts to the Active jobs that are overdue past <paramref name="margin"/> (SQL-translatable).
    /// </summary>
    /// <param name="margin">How late counts as overdue; defaults to <see cref="GraceMargin"/>.</param>
    public static IQueryable<ScheduledJob> WhereOverdue(
        this IQueryable<ScheduledJob> query, DateTime utcNow, TimeSpan? margin = null)
    {
        var cutoff = Threshold(utcNow, margin);
        return query.Where(x => x.Status == JobStatus.Active && x.NextRunAt != null && x.NextRunAt < cutoff);
    }

    /// <summary>
    /// Drops the jobs that are <b>executing right now</b>, per the dispatcher's
    /// <see cref="ScheduledJob.RunningSince"/> marker. Composed onto <see cref="WhereOverdue"/> by the alert
    /// path only — <b>a running job is late by every measurement and by no meaning.</b>
    /// <para>
    /// <b>Why this is a separate step and not folded into <see cref="WhereOverdue"/>.</b> The two callers want
    /// genuinely different answers. A dashboard column reports what the timing data <i>says</i>, and showing a
    /// long-running job as overdue there is informative — the operator sees it next to <c>LastRunAt</c> and has
    /// the context to interpret it. An alert asserts that <b>nothing is happening</b>, which about a job
    /// mid-execution is simply false. Keeping the exclusion here, named for what it does, means neither caller
    /// has to re-derive "overdue" and the difference between them stays one legible line at the call site.
    /// </para>
    /// <para>
    /// <b>Staleness bound (<paramref name="maxRunDuration"/>).</b> A marker is only honoured while it is
    /// plausible. A process killed mid-run leaves <c>RunningSince</c> set with nobody to clear it, and an
    /// unbounded marker would make that job permanently un-alertable — a <i>false negative</i>, which for a
    /// detector is far worse than the false positive it exists to prevent. Past the bound the marker is ignored
    /// and the job becomes alertable again, so the failure mode self-heals without a boot-time cleanup pass
    /// (which would have to race the scheduler starting).
    /// </para>
    /// </summary>
    /// <param name="maxRunDuration">How long a run may plausibly take before its marker is treated as stale.</param>
    public static IQueryable<ScheduledJob> WhereNotInFlight(
        this IQueryable<ScheduledJob> query, DateTime utcNow, TimeSpan maxRunDuration)
    {
        var staleAt = utcNow - maxRunDuration;
        return query.Where(x => x.RunningSince == null || x.RunningSince < staleAt);
    }
}