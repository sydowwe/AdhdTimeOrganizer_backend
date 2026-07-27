using AdhdTimeOrganizer.Scheduler.domain.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MojaDigitalnaFirma.Kernel.scheduling;
using Sydowwe.Framework.config.dependencyInjection;

namespace AdhdTimeOrganizer.Scheduler.application.job;

// GDPR Art. 5(1)(e) (storage limitation) — retention for the Scheduler's OWN run log (audit 2026-07
// L1/B3, owner decision D1). The scheduled_job_run ledger is append-only and would otherwise grow for
// the life of the deployment; it is technical/operational data of the same class as audit_log, so it
// gets the same 3-year horizon by default (RetentionOptions.DefaultRetentionYears) — NOT the 10-year
// business-audit one. The window is deployment-configurable via SchedulerRetentionOptions.
//
// Deletion rules (all three must hold for a row to be deleted):
//   1. Older than the configured horizon (by StartedAt — the run's semantic instant).
//   2. Not among its job's most-recent KeepLastN runs, so a job that fires only yearly never loses its
//      recent history to the age cutoff alone.
//   3. Not referenced by ANY other row's ReplaysRunId. The replay-lineage self-FK is
//      DeleteBehavior.Restrict on purpose (ledger invariant) — deleting a referenced row would abort
//      the whole batch with an FK violation. Excluding every referenced row (not just rows referenced
//      by survivors) keeps the single DELETE FK-safe even for replay-of-a-replay chains; a chain's
//      tail becomes unreferenced as its heads age out, so it is collected over successive monthly runs.
//
// All three compose into ONE ExecuteDeleteAsync. Kept as plain LINQ rather than routed through a shared
// generic helper: the age + keep-last-N half is four readable lines, while rule 3 — the part that
// actually differs per ledger — can't be shared anyway. The retention *policy* is shared
// (RetentionOptions); the query is not.
//
// Hard delete via ExecuteDeleteAsync is deliberate: ScheduledJobRun is [NoAudit] (a self-audit ledger),
// so bypassing the ChangeTracker/audit interceptor loses nothing — and a purge must not write audit
// rows about itself. Deletes ONLY ScheduledJobRun; the ScheduledJob registry row is never touched.
// Background-safe (no authenticated user).
//
// TODO (reminders follow-up 01, out of scope): per-user erasure. Purging on age does not reach a run
// log row on demand when a user is anonymized. That needs a Kernel erasure contract so EmployeeModule
// never references this module — a separate task.
public class PurgeExpiredRunLogsJobHandler(
    DbContext dbContext,
    IOptions<SchedulerRetentionOptions> options,
    ILogger<PurgeExpiredRunLogsJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "Scheduler.PurgeExpiredRunLogs";

    public string Key => HandlerKey;

    public Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct) => PurgeExpiredAsync(ct);

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var retention = options.Value;
        if (!retention.Enabled)
        {
            logger.LogDebug("Scheduler run-log retention purge is disabled by configuration; skipping.");
            return;
        }

        var cutoff = retention.CutoffUtc();
        var keepLastN = retention.KeepLastN;
        var runs = dbContext.Set<ScheduledJobRun>();

        var deleted = await runs
            .Where(r => r.StartedAt < cutoff)
            // Keep-last-N floor: deletable only when at least N runs of the same job are strictly newer.
            // A StartedAt tie counts as "not newer", which errs on keeping rows — acceptable slack.
            .Where(r => runs.Count(newer => newer.ScheduledJobId == r.ScheduledJobId && newer.StartedAt > r.StartedAt) >= keepLastN)
            // Replay-lineage guard (rule 3): never delete a row something still points at.
            .Where(r => !runs.Any(other => other.ReplaysRunId == r.Id))
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation(
                "Deleted {Count} scheduled_job_run row(s) past {Years}y retention (keep-last-{KeepLast}-per-job floor)",
                deleted, retention.RetentionYears, retention.KeepLastN);
    }
}