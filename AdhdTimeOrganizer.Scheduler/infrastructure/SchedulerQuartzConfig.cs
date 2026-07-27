using AdhdTimeOrganizer.Scheduler.application.job;
using MojaDigitalnaFirma.Kernel.scheduling;
using Quartz;

namespace AdhdTimeOrganizer.Scheduler.infrastructure;

/// <summary>
/// Scheduler owns the Quartz <i>configuration defaults</i> + the single generic dispatcher job. The host
/// keeps the one <c>services.AddQuartz(...)</c> call and invokes <see cref="AddSchedulerQuartzDefaults"/>
/// inside it; the existing per-module <c>q.AddJob&lt;DomainJob&gt;()</c> + trigger lines stay in the host
/// (they reference domain modules Scheduler must not <c>using</c>) until phase 05 migrates them to
/// <see cref="MojaDigitalnaFirma.Kernel.scheduling.IScheduler"/> registration.
/// </summary>
public static class SchedulerQuartzConfig
{
    /// <summary>
    /// Stable Quartz <see cref="JobKey"/> of the single generic dispatcher every recurring trigger points
    /// at. Lives in the <c>scheduler</c> group so it never collides with a domain job's identity.
    /// </summary>
    public static readonly JobKey DispatcherJobKey = new(nameof(ScheduledJobDispatcher), "scheduler");

    /// <summary>
    /// Quartz group every per-registration trigger lives in (the business <c>JobKey</c> is the trigger
    /// name). Keeps Scheduler's triggers from colliding with any domain job's own trigger identities.
    /// </summary>
    public const string TriggerGroup = "scheduler";

    /// <summary>
    /// <see cref="JobDataMap"/> key carrying the business <c>JobKey</c> the dispatcher must run. Every
    /// trigger (and the manual trigger-now fire) sets it, since all of them point at the single shared
    /// dispatcher job. Read by the dispatcher body in phase 03.
    /// </summary>
    public const string JobKeyDataKey = "scheduler.jobKey";

    /// <summary>
    /// <see cref="JobDataMap"/> key carrying the <c>TriggerSource</c> name on an off-schedule fire
    /// (trigger-now → <c>Manual</c>). Absent on a normal scheduled fire (the dispatcher defaults to
    /// <c>Scheduled</c>). Read by the dispatcher body in phase 03.
    /// </summary>
    public const string TriggerSourceDataKey = "scheduler.triggerSource";

    /// <summary>
    /// <see cref="JobDataMap"/> key carrying a payload override (raw JSON) for an off-schedule fire. When
    /// present the dispatcher (phase 03) uses it as both the context payload and the run's
    /// <c>PayloadSnapshotJson</c> instead of the registry <c>PayloadJson</c> — this is how a phase-04b
    /// replay re-runs with the <i>snapshotted</i> payload. Absent on a scheduled fire (uses the registry payload).
    /// </summary>
    public const string PayloadOverrideDataKey = "scheduler.payloadOverride";

    /// <summary>
    /// <see cref="JobDataMap"/> key carrying the id of the run a replay re-ran (phase-04b replay lineage),
    /// persisted onto the new run's <c>ReplaysRunId</c>. Also carries the auto-retry lineage: every retry in a
    /// chain links to the <b>original</b> failed run through this same column (no second linking column — see
    /// the retention purge's lineage exclusion). Absent on scheduled and trigger-now fires.
    /// </summary>
    public const string ReplaysRunIdDataKey = "scheduler.replaysRunId";

    /// <summary>
    /// <see cref="JobDataMap"/> key carrying the auto-retry attempt number (<c>1..MaxRetries</c>) on a
    /// <c>Retry</c> fire, persisted onto the run's <c>RetryAttempt</c>. Absent on the original fire (attempt 0).
    /// </summary>
    public const string RetryAttemptDataKey = "scheduler.retryAttempt";

    /// <summary>
    /// <see cref="JobDataMap"/> key carrying the job's max-retry cap at the time the retry chain started, so
    /// the failure-alerting seam can tell whether a <c>Failed</c> retry was the final attempt. Absent on the
    /// original fire.
    /// </summary>
    public const string RetryMaxDataKey = "scheduler.retryMax";

    /// <summary>
    /// <see cref="JobDataMap"/> key carrying a one-shot's fixed fire instant (UTC ticks) on the one-shot
    /// trigger. The dispatcher uses it as the scheduled fire time so both the first fire and a boot
    /// re-registration re-fire compute the <i>same</i> occurrence — letting the run-log dedup (which survives
    /// restarts) recognise the re-fire as a duplicate. Absent on recurring triggers.
    /// </summary>
    public const string RunOnceAtDataKey = "scheduler.runOnceAt";

    /// <summary>The <see cref="TriggerKey"/> for a registration's recurring trigger, keyed by its business <c>JobKey</c>.</summary>
    public static TriggerKey TriggerKeyFor(string jobKey) => new(jobKey, TriggerGroup);

    /// <summary>
    /// Builds the <see cref="JobDataMap"/> a delayed auto-retry fire carries: identifies the business job, flags
    /// the fire as <see cref="TriggerSource.Retry"/> (so the dispatcher leaves its <c>ScheduledFireTime</c> null
    /// and it is never deduped), carries the attempt number + max (for the alerting seam) and the original-run
    /// lineage id, and re-runs the exact payload the failed attempt used. Centralised here so the real
    /// <see cref="IJobRetryScheduler"/> and the dispatcher's tests agree on the key layout.
    /// </summary>
    public static JobDataMap BuildRetryDataMap(string jobKey, int attempt, int maxRetries, long lineageRunId, string? payloadJson)
    {
        var map = new JobDataMap
        {
            { JobKeyDataKey, jobKey },
            { TriggerSourceDataKey, nameof(TriggerSource.Retry) },
            { RetryAttemptDataKey, attempt },
            { RetryMaxDataKey, maxRetries },
            { ReplaysRunIdDataKey, lineageRunId }
        };
        if (!string.IsNullOrEmpty(payloadJson))
            map[PayloadOverrideDataKey] = payloadJson;
        return map;
    }

    /// <summary>The reusable Quartz config defaults, applied once inside the host's single AddQuartz call.</summary>
    public static void AddSchedulerQuartzDefaults(this IServiceCollectionQuartzConfigurator quartz)
    {
        // Persistent-store / clustering decision point: deliberately NOT enabled. This platform is a
        // single-node modular monolith, so the in-memory RAM job store is correct here — correctness
        // against double-fires lives in our Postgres run log + per-occurrence dedup, not in Quartz.
        // Revisit (Quartz clustering + a persistent store for cluster-wide DisallowConcurrent) ONLY if
        // the deployment ever becomes multi-node: a flagged, repo-wide decision, not made here.

        // Mirror the existing block: it sets no global misfire threshold, so keep Quartz's default rather
        // than inventing one. Per-trigger misfire *behaviour* is the MisfirePolicy concern handled when
        // triggers are created (02b/03); triggers run in UTC (set per-trigger, as the existing jobs do).

        // The single generic dispatcher job. Durable so it can exist with no trigger of its own — the
        // recurring triggers created per registration in 02b point at it. Its body is a stub until 03.
        quartz.AddJob<ScheduledJobDispatcher>(job => job
            .WithIdentity(DispatcherJobKey)
            .StoreDurably());
    }

    /// <summary>
    /// Hosted-service options for Scheduler's Quartz, centralised here so the host doesn't restate them:
    /// wait for running jobs to finish on shutdown (mirrors the existing block).
    /// </summary>
    public static void ConfigureSchedulerHostedService(QuartzHostedServiceOptions options)
    {
        options.WaitForJobsToComplete = true;
    }
}