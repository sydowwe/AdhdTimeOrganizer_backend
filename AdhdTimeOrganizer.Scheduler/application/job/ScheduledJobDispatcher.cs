using System.Diagnostics;
using AdhdTimeOrganizer.Scheduler.domain.entity;
using AdhdTimeOrganizer.Scheduler.domain.@enum;
using AdhdTimeOrganizer.Scheduler.infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quartz;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Scheduler.application.job;

/// <summary>
/// The single generic Quartz job every recurring trigger (and every trigger-now / replay fire) points at.
/// A fire resolves the keyed <see cref="IScheduledJobHandler"/> for the job, invokes it with <b>failure
/// isolation</b> and <b>per-key concurrency control</b>, and captures exactly one append-only
/// <see cref="ScheduledJobRun"/> at completion. It is the engine that turns "a trigger fired" into "a
/// handler ran, and here is the audited result".
/// <para>
/// <b>Generic by construction:</b> it knows only the <see cref="ScheduledJob"/> registry row, the run log,
/// and the <see cref="IScheduledJobHandler"/> contract — never a domain type. The work body belongs to the
/// owning module's handler. Runs with <b>no authenticated user</b>; handlers must be background-safe.
/// </para>
/// <para>
/// <b>Append-only:</b> no in-progress row is ever written — <see cref="StartedAt"/> is held in memory and
/// the row is inserted once at completion together with <see cref="ScheduledJobRun.FinishedAt"/> and a
/// terminal <see cref="RunOutcome"/>. A crash between invoke and insert leaves no row; recovery is the next
/// fire + the misfire policy. The registry's <c>LastRunAt</c>/<c>LastOutcome</c>/<c>NextRunAt</c> are
/// mutable observability (rewritten each fire); the run log is never updated.
/// </para>
/// <para>
/// <b>Single-node by design:</b> the in-process <see cref="IJobConcurrencyGate"/> is the authoritative
/// <c>DisallowConcurrent</c> enforcement and the run-log dedup is the safety net against the remaining
/// single-node double-fire cases (misfire catch-up, a manual fire overlapping a scheduled one). Correctness
/// lives in Postgres, not in Quartz — no persistent store / clustering. Multi-node would be the trigger to
/// revisit (Quartz clustering + a persistent store + a DB-backed in-flight lock); not built now.
/// </para>
/// <para>
/// <b>Failure alerting (follow-up 05):</b> a <b>terminal</b> failure raises a push alert through the
/// <see cref="IJobFailureNotifier"/> <c>Sydowwe.Framework.Contracts</c> seam (Scheduler references no delivery module). It fires only for
/// the <b>final</b> retry of a scheduled fire failing, or a <c>HandlerNotFound</c> misconfiguration — never a
/// non-final failure (a retry still queued) nor a manual/replay failure. Emission is gated on the job's
/// <see cref="ScheduledJob.AlertOnFailure"/> opt-out, fired <i>after</i> the run row commits, and
/// failure-isolated (a throwing notifier is caught, never fails the run) — see <c>EmitFailureAlertAsync</c>.
/// </para>
/// <para>
/// <b>Command-dispatch scope (follow-up 07):</b> a job body — and anything it calls — may dispatch
/// FastEndpoints commands (<c>new SomeCommand(…).ExecuteAsync(ct)</c>). The dispatcher guarantees the ambient
/// scope that needs; see <see cref="EnsureAmbientDispatchScope"/> for why it would otherwise fail silently.
/// </para>
/// </summary>
public sealed class ScheduledJobDispatcher(
    DbContext dbContext,
    IEnumerable<IScheduledJobHandler> handlers,
    IJobConcurrencyGate concurrencyGate,
    IJobRetryScheduler retryScheduler,
    IJobFailureNotifier failureNotifier,
    IJobAlertThrottle alertThrottle,
    ILogger<ScheduledJobDispatcher> logger,
    // Optional so a test can drive the dispatcher without a DI container; in every host both are always
    // injected (IHttpContextAccessor via AddHttpContextAccessor, IServiceProvider being the per-fire scope's
    // own provider). Null simply means "no ambient scope to install" — the pre-follow-up-07 behaviour.
    IHttpContextAccessor? httpContextAccessor = null,
    IServiceProvider? scopeProvider = null) : IJob
{
    /// <summary>
    /// Base backoff for the first auto-retry. Each further attempt doubles it (<c>base × 2^(attempt-1)</c>):
    /// attempt 1 ≈ 1 min, 2 ≈ 2 min, 3 ≈ 4 min — giving a struggling dependency progressively more time.
    /// </summary>
    private static readonly TimeSpan RetryBackoffBase = TimeSpan.FromMinutes(1);

    /// <summary>Cap on the doubling exponent so a large <c>MaxRetries</c> can't overflow the backoff (base × 2^10 ≈ 17 h).</summary>
    private const int MaxBackoffExponent = 10;

    public async Task Execute(IJobExecutionContext context)
    {
        // 0. Give this fire an ambient scope BEFORE anything else, so every step below — including the
        // handler body and everything it calls — can dispatch FastEndpoints commands.
        EnsureAmbientDispatchScope();

        var ct = context.CancellationToken;
        var dataMap = context.MergedJobDataMap;

        // 1. Identity — which business job is this fire for?
        var jobKey = dataMap.ContainsKey(SchedulerQuartzConfig.JobKeyDataKey)
            ? dataMap.GetString(SchedulerQuartzConfig.JobKeyDataKey)
            : null;
        if (string.IsNullOrEmpty(jobKey))
        {
            logger.LogWarning("ScheduledJobDispatcher fired with no {DataKey} in the data map; ignoring.",
                SchedulerQuartzConfig.JobKeyDataKey);
            return;
        }

        var job = await dbContext.Set<ScheduledJob>().FirstOrDefaultAsync(x => x.JobKey == jobKey, ct);
        if (job is null)
        {
            // No registry parent → no FK to hang a run row on. Orphaned trigger (should not happen on a
            // RAM store, where triggers exist only because registration created the row): log + return.
            logger.LogWarning("ScheduledJobDispatcher fired for unknown JobKey {JobKey}; no registry row, skipping.", jobKey);
            return;
        }

        var triggerSource = ParseTriggerSource(dataMap);
        var retryAttempt = ParseRetryAttempt(dataMap);
        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        // 1b. A removed job whose trigger still fired (e.g. a manual fire after RemoveJobAsync): record the
        // orphan as Skipped and return — never invoke a removed job's handler.
        if (job.Status == JobStatus.Removed)
        {
            await AppendSkipRunAsync(job, RunOutcome.Skipped, triggerSource, retryAttempt, correlationId, null,
                null, "OrphanedTrigger",
                "Trigger fired for a removed job.", ct);
            return;
        }

        // 1c. A queued backoff retry is INDEPENDENT of the job's (paused) recurring trigger — pausing a job
        // after a failure does not cancel its already-armed retry. Check the live status at retry-fire time and
        // skip (record Skipped) when the job is no longer Active; the Removed case is already handled above, so
        // this catches Paused. (Scheduled/manual/replay fires are unaffected: a paused job's recurring trigger
        // simply doesn't fire, and a manual/replay fire on a paused job is an intentional operator action.)
        if (triggerSource == TriggerSource.Retry && job.Status != JobStatus.Active)
        {
            logger.LogInformation("Auto-retry {Attempt} of {JobKey} skipped: job is no longer active ({Status}).",
                retryAttempt, jobKey, job.Status);
            await AppendSkipRunAsync(job, RunOutcome.Skipped, triggerSource, retryAttempt, correlationId, null,
                null, "JobNotActive",
                $"Auto-retry skipped: job is {job.Status}, not Active.", ct);
            return;
        }

        // 2. Resolve the keyed handler. A missing handler is a misconfiguration, not a crash: record Failed
        // with a clear ErrorType and return.
        var handler = ScheduledJobHandlerResolver.Resolve(handlers, job.HandlerKey);
        if (handler is null)
        {
            logger.LogError("No IScheduledJobHandler for handler key {HandlerKey} (job {JobKey}); recording misconfiguration.",
                job.HandlerKey, jobKey);
            var runId = await AppendSkipRunAsync(job, RunOutcome.Failed, triggerSource, retryAttempt, correlationId, null,
                null, "HandlerNotFound",
                $"No IScheduledJobHandler registered for key '{job.HandlerKey}'.", ct);
            // A misconfigured job never enters the retry loop, so the "only after the final retry" gate does
            // not apply — but "your job is silently misconfigured" is exactly what an unattended owner must
            // hear, so alert here too (best-effort, after the run row committed above).
            await EmitFailureAlertAsync(job, runId, "HandlerNotFound", ct);
            return;
        }

        // 3. Concurrency control — authoritative single-node in-process gate. Skip (don't queue) an
        // overlapping fire while another run for this JobKey is in flight. A retry respects this the same as
        // any other fire — a retry overlapping a live run is vetoed, not queued.
        var gated = job.DisallowConcurrent;
        if (gated && !concurrencyGate.TryAcquire(jobKey))
        {
            logger.LogInformation("Concurrent fire of {JobKey} vetoed by the in-process gate (DisallowConcurrent).", jobKey);
            await AppendSkipRunAsync(job, RunOutcome.Vetoed, triggerSource, retryAttempt, correlationId, null,
                null, "ConcurrencyVeto",
                "Overlapping fire skipped while a run was in flight (DisallowConcurrent).", ct);
            return;
        }

        try
        {
            await InvokeAndCaptureAsync(context, job, handler, jobKey, triggerSource, retryAttempt, correlationId, dataMap, ct);
        }
        finally
        {
            if (gated)
                concurrencyGate.Release(jobKey);
        }
    }

    /// <summary>
    /// Steps 4–7 (under the gate): build the context + read off-schedule overrides, run the misfire/catch-up
    /// dedup check, invoke with failure isolation, then insert the single terminal run row and update the
    /// registry's observability fields.
    /// </summary>
    private async Task InvokeAndCaptureAsync(
        IJobExecutionContext context, ScheduledJob job, IScheduledJobHandler handler, string jobKey,
        TriggerSource triggerSource, int retryAttempt, string correlationId, JobDataMap dataMap, CancellationToken ct)
    {
        // 4. Build the context. A scheduled fire carries its scheduled instant (used for dedup); a
        // manual/replay/retry fire is off-schedule → null, so it is never deduped (it is an intentional
        // re-fire). A one-shot is a scheduled fire whose instant is the fixed RunOnceAt (carried on the
        // trigger) rather than Quartz's computed time — so the on-time fire and a boot re-registration re-fire
        // dedup to the SAME occurrence, making the one-shot at-most-once across restarts via the run log.
        var scheduledFireTime = triggerSource == TriggerSource.Scheduled
            ? RunOnceInstant(dataMap) ?? context.ScheduledFireTimeUtc?.UtcDateTime
            : null;
        var actualFireTime = context.FireTimeUtc.UtcDateTime;

        // A replay supplies the snapshotted payload as an override (used as both the context payload and the
        // run's PayloadSnapshotJson); a plain fire uses the registry payload.
        var payloadOverride = dataMap.ContainsKey(SchedulerQuartzConfig.PayloadOverrideDataKey)
            ? dataMap.GetString(SchedulerQuartzConfig.PayloadOverrideDataKey)
            : null;
        var payloadJson = string.IsNullOrEmpty(payloadOverride) ? job.PayloadJson : payloadOverride;
        var replaysRunId = dataMap.ContainsKey(SchedulerQuartzConfig.ReplaysRunIdDataKey)
            ? dataMap.GetLong(SchedulerQuartzConfig.ReplaysRunIdDataKey)
            : (long?)null;

        // Misfire / catch-up dedup safety net. If this scheduled occurrence already produced an actual
        // execution (Succeeded/Failed), a double-fire/catch-up of the same instant must not re-invoke —
        // record it as Skipped and return. This app-level check is the common (sequential) path; its durable
        // backstop is the partial unique index on (ScheduledJobId, ScheduledFireTime) terminal rows, which
        // catches the racing-concurrent case the in-process gate covers only for DisallowConcurrent jobs
        // (handled at the insert in step 7).
        if (scheduledFireTime is not null)
        {
            var alreadyRan = await dbContext.Set<ScheduledJobRun>().AnyAsync(
                r => r.ScheduledJobId == job.Id
                     && r.ScheduledFireTime == scheduledFireTime
                     && (r.Outcome == RunOutcome.Succeeded || r.Outcome == RunOutcome.Failed), ct);
            if (alreadyRan)
            {
                logger.LogInformation("Scheduled occurrence of {JobKey} at {FireTime:o} already ran; skipping duplicate.",
                    jobKey, scheduledFireTime);
                await AppendSkipRunAsync(job, RunOutcome.Skipped, triggerSource, retryAttempt, correlationId, scheduledFireTime,
                    payloadJson, "DuplicateFire",
                    "Scheduled occurrence already executed (misfire/catch-up dedup).", ct);
                return;
            }
        }

        var jobContext = new ScheduledJobContext
        {
            ScheduledFireTimeUtc = scheduledFireTime,
            ActualFireTimeUtc = actualFireTime,
            JobKey = jobKey,
            PayloadJson = payloadJson,
            CorrelationId = correlationId,
            TriggerSource = triggerSource
        };

        // 5. Capture start in memory (no row yet — every run row is terminal) and publish the in-flight marker
        // BEFORE invoking, so the overdue sweep can tell "executing" from "never fired" (see MarkRunningAsync).
        var startedAt = DateTime.UtcNow;
        await MarkRunningAsync(job, startedAt, jobKey, ct);

        try
        {
            await InvokeCaptureAndAlertAsync(
                context, job, handler, jobKey, triggerSource, retryAttempt, correlationId,
                jobContext, scheduledFireTime, payloadJson, replaysRunId, startedAt, ct);
        }
        finally
        {
            // Every exit path clears the marker — including the early DuplicateFire return and any unexpected
            // throw from the persistence steps. Cheap when step 8 already cleared it (see ClearRunningAsync).
            await ClearRunningAsync(job, jobKey);
        }
    }

    /// <summary>Steps 6–9: invoke, persist the terminal run row, refresh observability, then retry/alert.</summary>
    private async Task InvokeCaptureAndAlertAsync(
        IJobExecutionContext context, ScheduledJob job, IScheduledJobHandler handler, string jobKey,
        TriggerSource triggerSource, int retryAttempt, string correlationId, ScheduledJobContext jobContext,
        DateTime? scheduledFireTime, string? payloadJson, long? replaysRunId, DateTime startedAt, CancellationToken ct)
    {
        // 6. Invoke with failure isolation. A throwing handler is recorded as Failed and never bubbles out
        // of the Quartz job; the host and sibling jobs survive.
        RunOutcome outcome;
        string? errorType = null;
        string? errorMessage = null;
        try
        {
            await handler.ExecuteAsync(jobContext, ct);
            outcome = RunOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            outcome = RunOutcome.Failed;
            errorType = ex.GetType().FullName;
            errorMessage = ex.Message; // handlers own message hygiene (no PII) — see the contract docs.
            logger.LogError(ex, "Handler {HandlerKey} failed for job {JobKey} (correlation {CorrelationId}).",
                job.HandlerKey, jobKey, correlationId);
        }

        // 7. Persist the one terminal run row in its OWN save first. It is append-only (no concurrency
        // token) so it cannot conflict with a concurrent control op on the registry row, and committing it
        // independently guarantees a successfully-run handler always leaves an audited ledger row — even if
        // the registry observability update below loses an optimistic-concurrency race.
        var finishedAt = DateTime.UtcNow;
        var runRow = new ScheduledJobRun
        {
            ScheduledJobId = job.Id,
            JobKeySnapshot = job.JobKey,
            HandlerKeySnapshot = job.HandlerKey,
            ScheduledFireTime = scheduledFireTime,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Outcome = outcome,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            TriggerSource = triggerSource,
            RetryAttempt = retryAttempt,
            CorrelationId = correlationId,
            PayloadSnapshotJson = payloadJson,
            ReplaysRunId = replaysRunId
        };
        dbContext.Add(runRow);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The partial unique index (ScheduledJobId, ScheduledFireTime WHERE outcome terminal) caught a
            // racing second actual execution of the same scheduled occurrence — the app-level dedup above and
            // the in-process gate both missed it (concurrent same-occurrence fire of a non-DisallowConcurrent
            // job). The DB is the durable backstop: drop the would-be duplicate ledger row and record a
            // DuplicateFire Skipped instead. The handler already ran here; the index guarantees the *ledger*
            // stays one-execution-per-occurrence even when two fires overlap.
            dbContext.Entry(runRow).State = EntityState.Detached;
            logger.LogInformation(ex,
                "Run row for scheduled occurrence of {JobKey} at {FireTime:o} hit the dedup unique index; recording duplicate as Skipped.",
                jobKey, scheduledFireTime);
            await AppendSkipRunAsync(job, RunOutcome.Skipped, triggerSource, retryAttempt, correlationId, scheduledFireTime,
                payloadJson, "DuplicateFire",
                "Scheduled occurrence already executed (DB dedup; concurrent same-occurrence fire).", ct);
            return;
        }

        // 8. Best-effort registry observability update (a SECOND save). LastRunAt/LastOutcome/NextRunAt are
        // disposable observability that the run log already captured authoritatively; if a concurrent control
        // op (pause/resume/remove/re-register) bumped the registry's row_version between our tracked load and
        // here, swallow the concurrency conflict rather than rolling back — the ledger truth is already
        // committed and these fields are refreshed on the next fire.
        job.LastRunAt = startedAt;
        job.LastOutcome = outcome;
        // The run is over — retire the in-flight marker in the SAME save as the other observability fields, so
        // the common path costs no extra round trip (the finally-block clear then finds nothing to do).
        job.RunningSince = null;
        // Only a scheduled fire owns NextRunAt; a manual/replay one-off must not touch it (the recurring
        // trigger still owns the schedule).
        if (triggerSource == TriggerSource.Scheduled)
            job.NextRunAt = await NextFireTimeAsync(context, jobKey, ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogInformation(ex,
                "Registry observability update for {JobKey} lost a concurrency race; run row already committed, skipping.",
                jobKey);
        }

        // 9. Auto-retry on failure. Only an unattended SCHEDULED run (or a prior RETRY continuing that chain)
        // is retried — a Manual/Replay failure is not (an operator is watching, per the retry contract). Each
        // retry is an off-schedule re-fire (TriggerSource.Retry ⇒ null ScheduledFireTime), so it is NEVER
        // matched by the scheduled-occurrence dedup / the terminal-only unique index and is never dropped as a
        // DuplicateFire. It also respects DisallowConcurrent (the gate above vetoes an overlap like any fire).
        if (outcome == RunOutcome.Failed && triggerSource is TriggerSource.Scheduled or TriggerSource.Retry)
        {
            if (retryAttempt < job.MaxRetries)
            {
                // A retry that could NOT be armed makes this failure terminal after all — there is no further
                // attempt coming — so it must alert like any final failure. Without this, a job whose retry
                // arming throws goes silent until its next scheduled fire: up to a month for the monthly
                // purges, a year for the calendar-boundary jobs, and FOREVER for a one-shot (it has no next
                // fire). The alert invariant is "the failure is terminal", not merely "retries are exhausted".
                if (!await ScheduleRetryAsync(jobKey, retryAttempt, job.MaxRetries, replaysRunId ?? runRow.Id, payloadJson, ct))
                    await EmitFailureAlertAsync(job, runRow.Id, errorType, ct);
            }
            else
            {
                // FINAL attempt failed (retries exhausted, or MaxRetries == 0 ⇒ retries disabled) — alert now
                // (scheduler follow-up 05). We must NOT alert on a non-final Failed run (one with a retry still
                // to come): the retry branch above handles those silently. That distinction can't be recomputed
                // cheaply from run rows alone, which is why attempt + max are carried on this fire (run row
                // RetryAttempt + job.MaxRetries in scope). Best-effort, fired after the run row committed above.
                logger.LogWarning(
                    "Job {JobKey} failed on its FINAL attempt ({Attempt}/{Max}); no further auto-retry, alerting.",
                    jobKey, retryAttempt, job.MaxRetries);
                await EmitFailureAlertAsync(job, runRow.Id, errorType, ct);
            }
        }
    }

    /// <summary>
    /// Publishes the <see cref="ScheduledJob.RunningSince"/> marker before the handler is invoked, so an
    /// external observer can tell <b>"executing"</b> from <b>"never fired"</b>.
    /// <para>
    /// <b>The trap this closes.</b> <c>NextRunAt</c> is recomputed only in step 8, <i>after</i> the handler
    /// returns. For the whole duration of a run the registry therefore still advertises the fire that is
    /// currently executing — a job with a 4-minute body looks 4 minutes overdue while it works perfectly. The
    /// pull dashboard can live with that (an operator reads it next to <c>LastRunAt</c>); the overdue sweep
    /// cannot, because an alert asserts that nothing is happening. Before this marker the sweep could only
    /// guess with a wide margin, which traded false pages for slow detection and still broke for any job
    /// slower than the guess.
    /// </para>
    /// <para>
    /// <b>Best-effort by design.</b> Its own save, and a lost optimistic-concurrency race is swallowed exactly
    /// like step 8's: a concurrent control op (pause/resume/re-register) must never prevent a job from running,
    /// and the marker is advisory. Failing to set it costs at most one spurious overdue alert for a slow job —
    /// strictly better than failing the run over an observability write.
    /// </para>
    /// </summary>
    private async Task MarkRunningAsync(ScheduledJob job, DateTime startedAt, string jobKey, CancellationToken ct)
    {
        job.RunningSince = startedAt;
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogInformation(ex,
                "In-flight marker for {JobKey} lost a concurrency race; running anyway (the marker is advisory).",
                jobKey);
        }
    }

    /// <summary>
    /// Retires the in-flight marker on <b>every</b> exit path — the normal completion (where step 8 already
    /// cleared it in its own save, making this a no-op), the early <c>DuplicateFire</c> return, and any
    /// unexpected throw out of the persistence steps.
    /// <para>
    /// A marker that survives the run would make the job invisible to the overdue sweep, which is a
    /// <b>false negative</b> — the one failure mode a detector must not have. The sweep's staleness bound is
    /// the backstop for the case no <c>finally</c> can reach (the process being killed mid-run), so between the
    /// two the marker can never permanently shield a job.
    /// </para>
    /// </summary>
    private async Task ClearRunningAsync(ScheduledJob job, string jobKey)
    {
        if (job.RunningSince is null)
            return; // already retired by step 8's save — the common path pays nothing.

        job.RunningSince = null;
        try
        {
            // CancellationToken.None deliberately: this runs in a finally, and a cancelled fire is exactly
            // when the marker MUST still be retired.
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never bubble out of the Quartz job over an observability write — and never out of a finally,
            // where it would mask the original exception. The staleness bound collects what this misses.
            logger.LogWarning(ex,
                "Could not clear the in-flight marker for {JobKey}; the overdue sweep's staleness bound will retire it.",
                jobKey);
        }
    }

    /// <summary>
    /// Arms the next backoff retry via the durable dispatcher one-shot (never blocking the worker thread). A
    /// scheduling failure is logged, not thrown: the failed run is already audited, and crashing the Quartz job
    /// over a best-effort re-fire would be worse. The whole chain links to the ORIGINAL run through
    /// <c>ReplaysRunId</c> (<paramref name="lineageRunId"/>).
    /// </summary>
    /// <returns>
    /// <c>true</c> when the retry was armed; <c>false</c> when arming failed, which makes the just-recorded
    /// failure <b>terminal</b> — the caller then alerts on it, so a lost retry can't swallow the alert until
    /// the next scheduled fire (a one-shot would never get one).
    /// </returns>
    private async Task<bool> ScheduleRetryAsync(
        string jobKey, int failedAttempt, int maxRetries, long lineageRunId, string? payloadJson, CancellationToken ct)
    {
        var nextAttempt = failedAttempt + 1;
        var delay = TimeSpan.FromTicks(RetryBackoffBase.Ticks << Math.Min(nextAttempt - 1, MaxBackoffExponent));
        try
        {
            await retryScheduler.ScheduleAsync(
                new RetryFireRequest(jobKey, nextAttempt, maxRetries, delay, lineageRunId, payloadJson), ct);
            logger.LogInformation("Armed auto-retry {Attempt}/{Max} of {JobKey} in {Delay}.",
                nextAttempt, maxRetries, jobKey, delay);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to arm auto-retry {Attempt}/{Max} of {JobKey}; retry is lost, treating the failure as terminal and alerting.",
                nextAttempt, maxRetries, jobKey);
            return false;
        }
    }

    /// <summary>
    /// Inserts a single terminal run row for a non-invoking outcome (orphan / missing handler / concurrency
    /// veto / dedup skip). These do <b>not</b> touch the registry's <c>LastRunAt</c>/<c>LastOutcome</c>/
    /// <c>NextRunAt</c> — those reflect actual executions; the in-flight or already-completed run owns them.
    /// </summary>
    /// <returns>The id of the inserted run row (so a caller that alerts on it can reference the ledger row).</returns>
    private async Task<long> AppendSkipRunAsync(
        ScheduledJob job, RunOutcome outcome, TriggerSource triggerSource, int retryAttempt, string correlationId,
        DateTime? scheduledFireTime, string? payloadSnapshotJson, string errorType, string errorMessage,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var runRow = new ScheduledJobRun
        {
            ScheduledJobId = job.Id,
            JobKeySnapshot = job.JobKey,
            HandlerKeySnapshot = job.HandlerKey,
            ScheduledFireTime = scheduledFireTime,
            StartedAt = now,
            FinishedAt = now,
            Outcome = outcome,
            ErrorType = errorType,
            ErrorMessage = errorMessage,
            TriggerSource = triggerSource,
            RetryAttempt = retryAttempt,
            CorrelationId = correlationId,
            PayloadSnapshotJson = payloadSnapshotJson
        };
        dbContext.Add(runRow);
        await dbContext.SaveChangesAsync(ct);
        return runRow.Id;
    }

    /// <summary>
    /// Announce a terminal job failure through the <see cref="IJobFailureNotifier"/> seam — <b>best-effort and
    /// failure-isolated</b>. Gated on the job's <see cref="ScheduledJob.AlertOnFailure"/> opt-out and then on
    /// the per-key <see cref="IJobAlertThrottle"/> (at most one alert per job per window, so a job failing
    /// every few minutes can't flood every Admin's mailbox). The payload is PII-free (job key / owner / error
    /// TYPE / run id / timestamp — never the raw message). A throwing notifier is caught and logged: the run
    /// row is already committed (the ledger is the truth), so an alert failure must never mark the run
    /// differently or bubble out of the Quartz job — exactly like the dispatcher's other best-effort steps.
    /// </summary>
    private async Task EmitFailureAlertAsync(ScheduledJob job, long runId, string? errorType, CancellationToken ct)
    {
        if (!job.AlertOnFailure)
            return;

        // Throttle the NOTIFICATION only — the failure is already recorded in the run log either way, so a
        // suppressed alert loses no information, just the repeat page.
        if (!alertThrottle.ShouldAlert(job.JobKey, DateTime.UtcNow))
        {
            logger.LogInformation(
                "Failure alert for job {JobKey} (run {RunId}) suppressed: already alerted within the throttle window. The run log still records the failure.",
                job.JobKey, runId);
            return;
        }

        try
        {
            await failureNotifier.NotifyJobFailedAsync(new JobFailureAlert
            {
                // The dispatcher only ever raises the ran-and-failed kind; JobAlertKind.Overdue belongs to
                // OverdueJobSweepJobHandler, which shares this seam (follow-up 08).
                Kind = JobAlertKind.TerminalFailure,
                JobKey = job.JobKey,
                OwnerModule = job.OwnerModule,
                ErrorType = errorType,
                RunId = runId,
                DetectedAtUtc = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failure alert for job {JobKey} (run {RunId}) could not be delivered; the failed run is recorded, alert is lost.",
                job.JobKey, runId);
        }
    }

    /// <summary>
    /// Points an ambient <see cref="HttpContext"/> at <b>this fire's own DI scope</b>, so a job body may
    /// dispatch FastEndpoints commands.
    /// <para>
    /// <b>The trap this closes.</b> The FastEndpoints in-process command bus resolves a command handler's
    /// <b>scoped</b> services from <c>IHttpContextAccessor.HttpContext.RequestServices</c>, and falls back to
    /// the <b>root</b> provider when there is no ambient context — where resolving a scoped <c>DbContext</c>
    /// fails outright (and throws by construction with scope validation on). A Quartz fire has no ambient
    /// <c>HttpContext</c>: the job is resolved into a per-fire scope and invoked directly, with nothing setting
    /// the accessor. So <i>every</i> command dispatched from a job body used to throw. The dangerous half of
    /// that is the callers who <b>catch</b> it: a best-effort <c>try/catch</c> turns a hard failure into a
    /// permanent silent degradation (Zmluvy's gestor resolver fell through to its admin fallback forever; the
    /// notification name enricher dropped every employee name from background sends). The host's startup
    /// seeding block already carries the same workaround for the same reason — this is that fix, moved to the
    /// one class every scheduled job flows through.
    /// </para>
    /// <para>
    /// <b><c>??=</c>, never <c>=</c>.</b> The accessor is <c>AsyncLocal</c>-backed, so the context set here is
    /// confined to this fire's async flow and cannot leak into another fire. But a fire triggered inline on a
    /// request thread would already carry a real request context, and overwriting that would sever the caller's
    /// request services (and its principal) — so an existing context always wins.
    /// </para>
    /// </summary>
    private void EnsureAmbientDispatchScope()
    {
        if (httpContextAccessor is null || scopeProvider is null)
            return;

        httpContextAccessor.HttpContext ??= new DefaultHttpContext { RequestServices = scopeProvider };
    }

    private static TriggerSource ParseTriggerSource(JobDataMap dataMap) =>
        dataMap.ContainsKey(SchedulerQuartzConfig.TriggerSourceDataKey)
        && Enum.TryParse<TriggerSource>(dataMap.GetString(SchedulerQuartzConfig.TriggerSourceDataKey), out var source)
            ? source
            : TriggerSource.Scheduled;

    /// <summary>The auto-retry attempt this fire represents (0 for the original scheduled/manual/replay fire).</summary>
    private static int ParseRetryAttempt(JobDataMap dataMap) =>
        dataMap.ContainsKey(SchedulerQuartzConfig.RetryAttemptDataKey)
            ? dataMap.GetInt(SchedulerQuartzConfig.RetryAttemptDataKey)
            : 0;

    /// <summary>A one-shot's fixed fire instant carried on the trigger (UTC ticks), or null for a recurring fire.</summary>
    private static DateTime? RunOnceInstant(JobDataMap dataMap) =>
        dataMap.ContainsKey(SchedulerQuartzConfig.RunOnceAtDataKey)
            ? new DateTime(dataMap.GetLong(SchedulerQuartzConfig.RunOnceAtDataKey), DateTimeKind.Utc)
            : null;

    private static async Task<DateTime?> NextFireTimeAsync(IJobExecutionContext context, string jobKey, CancellationToken ct)
    {
        var trigger = await context.Scheduler.GetTrigger(SchedulerQuartzConfig.TriggerKeyFor(jobKey), ct);
        return trigger?.GetNextFireTimeUtc()?.UtcDateTime;
    }
}