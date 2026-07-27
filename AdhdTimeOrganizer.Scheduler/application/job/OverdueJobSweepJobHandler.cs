using AdhdTimeOrganizer.Scheduler.application.dashboard;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Scheduler.application.job;

// The active overdue sweep (scheduler follow-up 08, from 06/D4) — the push half of the "never fires" gap.
//
// THE GAP IT CLOSES. Follow-up 05's alerting fires when a job RUNS AND FAILS. It cannot fire for a job that
// never runs at all: the scheduler process is down, the owning module's registrar never ran (a startup-ordering
// bug, an exception swallowed during boot), the RAM-store trigger was lost on restart, or a ScheduledJob row
// outlived the registration that created it. None of those produce a Failed run row, so the dispatcher's
// EmitFailureAlertAsync is never reached and nobody is paged. Detection was pull-only: an admin had to open the
// 04a health dashboard and notice a NextRunAt in the past. This handler makes that same observation on a timer
// and pushes it through the SAME IJobFailureNotifier seam.
//
// ── The five design questions from the prompt, settled here rather than left implicit ──
//
// Q1. WHAT IF THE SWEEP ITSELF CAN'T RUN? ACCEPTED AND DOCUMENTED, NOT SOLVED.
//   This is the same blind spot one level up: if the sweep's own trigger is lost, it detects nothing —
//   including its own absence — and if the whole process is down, nothing in-process can page anyone anyway. A
//   self-healing detector is a contradiction; the honest fix is an EXTERNAL liveness probe (an uptime monitor
//   hitting /health), which is infrastructure, not application code, and out of scope for this module. So the
//   sweep is explicitly a detector of *individual* job silence, not of scheduler-wide death. This is the same
//   class of accepted single-node limitation as the RAM job store (see the module's B4/B5 notes) and is
//   recorded as such in docs/summary.md — do not file it as a bug.
//   One thing it DOES cover: a process that restarts and comes back with a half-registered scheduler. The
//   registry rows survive in Postgres with their stale NextRunAt, so the first tick after recovery reports
//   every job whose registration didn't come back.
//
// Q2. PAUSED / REMOVED JOBS. Handled by construction, not by a second filter here — OverduePolicy.WhereOverdue
//   restricts to Status == Active with a non-null NextRunAt, and SchedulerService nulls NextRunAt on both
//   pause and remove. Pinned by a test rather than left to inspection.
//
// Q2b. RUNNING JOBS (not in the original five — found while building, and the sharpest edge here).
//   NextRunAt is recomputed only AFTER a handler returns, so for its entire execution a job still advertises
//   the fire it is currently servicing: a 4-minute body looks 4 minutes overdue while working perfectly. The
//   pull dashboard can live with that; an alert cannot, because it asserts that nothing is happening. The
//   first cut papered over it with a wide 15-minute margin, which is a guess that buys slow detection and
//   breaks anyway for a job slower than the guess. The real fix is a fact, not an inference:
//   ScheduledJob.RunningSince, written by the dispatcher before it invokes and cleared when the run ends, and
//   excluded here via WhereNotInFlight. The margin then shrinks to a 5-minute skew cushion.
//   The marker's own failure mode — a process killed mid-run leaves it set forever, which would make that job
//   permanently un-alertable (a FALSE NEGATIVE, the one thing a detector must not have) — is bounded by
//   MaxRunHours rather than a boot-time cleanup pass, which would have to race the scheduler starting.
//
// Q3. DISTINCT NOTIFICATION TYPE, SHARED SEAM. A NEW NotificationType.ScheduledJobOverdue (silence and error
//   are different problems with different fixes, and one inbox row for both makes triage harder), but the SAME
//   IJobFailureNotifier / JobFailureAlert — widened, never duplicated, so Core.Scheduler still references no
//   delivery module. The widening was real work, not a rename: JobFailureAlert.RunId was `required long` and an
//   overdue job has no run row BY DEFINITION, so it is now `long?`; FailedAtUtc became DetectedAtUtc (honest for
//   both kinds) and ExpectedRunAtUtc was added to carry the fire that was missed. The kind travels as an
//   explicit JobAlertKind rather than being inferred from `RunId is null`.
//
// Q4. CADENCE. Config-bindable (OverdueJobSweepOptions, default every 10 min), mirroring ReminderScanOptions.
//   Cadence is only detection latency; the throttle decides re-alert frequency, so it can stay simple.
//
// Q5. NO "RECOVERED" NOTIFICATION (v1). A job that fires again silently stops being overdue: it disappears from
//   the next sweep and from the health view, and the admin who acted on the alert sees the status change there.
//   A resolution message is an easy future add if anyone asks for it; shipping it now would double the alert
//   volume for a signal nobody has asked to receive.
//
// The sweep does NOT exclude itself from its own scan: it carries its own RunningSince marker while it
// executes, so WhereNotInFlight filters it out like any other running job — and on the day it is late but
// alive, an operator should hear that.
public class OverdueJobSweepJobHandler(
    DbContext dbContext,
    IJobFailureNotifier failureNotifier,
    IJobAlertThrottle alertThrottle,
    IOptions<OverdueJobSweepOptions> options,
    ILogger<OverdueJobSweepJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Scheduler.OverdueJobSweep";

    /// <summary>The <see cref="JobFailureAlert.ErrorType"/> marker an overdue alert carries (it has no exception).</summary>
    public const string OverdueErrorType = "Overdue";

    public string Key => HandlerKey;

    public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct) => SweepAsync(ct);

    public async Task SweepAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogDebug("Overdue job sweep is disabled by configuration; skipping.");
            return;
        }

        var now = DateTime.UtcNow;

        // ONE definition of overdue — the dashboard's predicate, with the sweep's own margin — then the
        // in-flight exclusion that separates "never fired" from "still running" (a running job is late by
        // every measurement and by no meaning; see OverduePolicy.WhereNotInFlight).
        // AlertOnFailure is the same per-job opt-out the dispatcher honours: a job whose owner said "don't page
        // me when this breaks" must not be paged when it goes quiet either.
        var overdue = await dbContext.Set<ScheduledJob>()
            .WhereOverdue(now, settings.AlertMargin)
            .WhereNotInFlight(now, settings.MaxRunDuration)
            .Where(x => x.AlertOnFailure)
            .Select(x => new OverdueJob(x.JobKey, x.OwnerModule, x.NextRunAt!.Value))
            .ToListAsync(ct);

        if (overdue.Count == 0)
            return;

        logger.LogWarning(
            "Overdue sweep found {Count} Active job(s) past their expected fire time by more than {Margin}.",
            overdue.Count, settings.AlertMargin);

        foreach (var job in overdue)
            await EmitOverdueAlertAsync(job, now, settings, ct);
    }

    /// <summary>
    /// Announce one overdue job — throttled, and <b>failure-isolated per job</b> so a notifier that throws on
    /// one alert can't abandon the rest of the sweep. Unlike the dispatcher there is no ledger row backing this
    /// up, so a lost alert really is lost information; it is still not worth failing the sweep over, because a
    /// still-overdue job is re-reported on the next tick once the throttle window elapses.
    /// </summary>
    private async Task EmitOverdueAlertAsync(OverdueJob job, DateTime now, OverdueJobSweepOptions settings, CancellationToken ct)
    {
        // Bucketed under a kind-prefixed key so an overdue alert and a failure alert for the SAME job never
        // suppress one another — they are different conditions and an operator needs both.
        if (!alertThrottle.ShouldAlert(ThrottleKey(job.JobKey), now, settings.AlertThrottleWindow))
            return;

        try
        {
            await failureNotifier.NotifyJobFailedAsync(new JobFailureAlert
            {
                Kind = JobAlertKind.Overdue,
                JobKey = job.JobKey,
                OwnerModule = job.OwnerModule,
                ErrorType = OverdueErrorType,
                RunId = null, // no run row exists — that IS the condition
                DetectedAtUtc = now,
                ExpectedRunAtUtc = job.ExpectedRunAt
            }, ct);
        }
        catch (Exception ex)
        {
            // JobKey / OwnerModule are technical identifiers, not PII.
            logger.LogError(ex,
                "Overdue alert for job {JobKey} (expected {ExpectedRunAt:o}) could not be delivered; it stays visible in the health view.",
                job.JobKey, job.ExpectedRunAt);
        }
    }

    /// <summary>The throttle bucket for overdue alerts, namespaced away from the dispatcher's failure buckets.</summary>
    public static string ThrottleKey(string jobKey) => $"overdue:{jobKey}";

    private sealed record OverdueJob(string JobKey, string OwnerModule, DateTime ExpectedRunAt);
}