namespace MojaDigitalnaFirma.Kernel.scheduling;

/// <summary>
/// Cross-module seam by which the Scheduler substrate <b>announces</b> that a recurring job needs attention —
/// without the Scheduler ever depending on a delivery module. The Scheduler (producer) calls this; a consumer
/// (Core.Notifications) implements it and turns the alert into a real notification. The arrow points
/// Scheduler → <c>Kernel</c> ← Notifications, exactly like <see cref="IScheduler"/> / <see cref="IScheduledJobHandler"/>.
/// <para>
/// Fired only for a problem an <i>unattended</i> owner needs to hear about — see <see cref="JobAlertKind"/>
/// for the two modes:
/// <list type="bullet">
/// <item><see cref="JobAlertKind.TerminalFailure"/> — a scheduled/retry run that fails on its <b>final</b>
/// attempt (auto-retries exhausted, retries disabled, or a retry that could not be armed), or a job that is
/// silently <b>misconfigured</b> (its handler can't be resolved). A manual "trigger now" / replay failure does
/// NOT alert — an operator is already watching.</item>
/// <item><see cref="JobAlertKind.Overdue"/> — an <c>Active</c> job that <b>never fired</b>, raised by the
/// overdue sweep (follow-up 08). There is no run row, so <see cref="JobFailureAlert.RunId"/> is null.</item>
/// </list>
/// A job may opt out of both entirely via <see cref="RecurringJobRegistration.AlertOnFailure"/>.
/// </para>
/// <para>
/// <b>Best-effort by contract:</b> the run log is the source of truth; the alert is a courtesy. The producer
/// fires it only <i>after</i> the run row is committed and isolates failures — a throwing notifier must never
/// fail the run. An implementation must be safe to call with <b>no authenticated user</b> (it is invoked from
/// a background Quartz job).
/// </para>
/// </summary>
public interface IJobFailureNotifier
{
    Task NotifyJobFailedAsync(JobFailureAlert alert, CancellationToken ct = default);
}