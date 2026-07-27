using AdhdTimeOrganizer.Scheduler.application.job;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MojaDigitalnaFirma.Kernel.scheduling;
using Quartz;
using IScheduler = MojaDigitalnaFirma.Kernel.scheduling.IScheduler;

namespace AdhdTimeOrganizer.Scheduler.infrastructure.scheduling;

/// <summary>
/// Startup reconciliation seam for the Scheduler module's <b>own</b> recurring jobs — the module dogfoods
/// its substrate for its self-retention purge (<see cref="PurgeExpiredRunLogsJobHandler"/>) and its overdue
/// sweep (<see cref="OverdueJobSweepJobHandler"/>). Mirrors
/// <c>CoreScheduledJobsRegistrar</c>: on boot it (re-)registers each owned job via the
/// <see cref="IScheduler"/> contract; <see cref="IScheduler.RegisterRecurringJobAsync"/> is an idempotent
/// upsert by <c>JobKey</c>, so re-running on every boot converges the registry + Quartz triggers with no
/// duplicates (and is <i>required</i>, since the RAM job store drops all triggers on restart).
/// <para>
/// Registering the Scheduler's own maintenance job is not a domain dependency — the handler touches only
/// <c>scheduled_job_run</c>. Wired by the host that owns the Quartz substrate (e.g. HBCleaning); a portal
/// without it is covered by the <see cref="ISchedulerFactory"/> no-op guard below.
/// </para>
/// </summary>
public sealed class SchedulerScheduledJobsRegistrar(
    IServiceProvider services,
    IOptions<OverdueJobSweepOptions> sweepOptions,
    ILogger<SchedulerScheduledJobsRegistrar> logger) : IHostedService
{
    /// <summary>
    /// The recurring jobs owned by the Scheduler module itself, registered on every boot. A method rather
    /// than a static list because the sweep's cadence is deployment-configurable.
    /// </summary>
    public static IReadOnlyList<RecurringJobRegistration> BuildRegistrations(OverdueJobSweepOptions sweep) =>
    [
        new()
        {
            JobKey = PurgeExpiredRunLogsJobHandler.HandlerKey,
            HandlerKey = PurgeExpiredRunLogsJobHandler.HandlerKey,
            OwnerModule = "Scheduler",
            // Monthly on the 1st at 03:30 UTC — offset from Core.PurgeExpiredAuditLogs (03:00) so the two
            // purges never contend on the same fire instant. Idempotent and cheap when nothing has aged
            // out, so a missed run self-heals the next month. UTC is correct here (pure maintenance —
            // nothing wall-clock-anchored).
            Schedule = ScheduleSpec.FromCron("0 30 3 1 * ?"),
            DisallowConcurrent = true,
            Description = "GDPR Art. 5(1)(e): purge scheduled_job_run rows past 3y retention, keeping each job's last 20 runs and replay-lineage targets."
        },
        new()
        {
            JobKey = OverdueJobSweepJobHandler.HandlerKey,
            HandlerKey = OverdueJobSweepJobHandler.HandlerKey,
            OwnerModule = "Scheduler",
            // Every 10 minutes by default (configurable) — the push half of the "never fires" gap.
            Schedule = sweep.ToScheduleSpec(),
            DisallowConcurrent = true,
            // Read-only + throttled, so a missed tick costs nothing but latency: the next one re-reports
            // every job that is still overdue. FireAndProceed (the contract default) is therefore right —
            // one catch-up sweep on recovery, not a backlog of them.
            // AlertOnFailure stays default-true: if the detector itself starts throwing, that is precisely
            // the kind of silent degradation it exists to prevent, so it must page like any other job.
            Description = "Detects Active jobs that never fired (stale NextRunAt) and alerts via IJobFailureNotifier — the push half of overdue detection."
        }
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        // The Quartz/Scheduler substrate is a host-level opt-in: a portal that doesn't wire it has no
        // ISchedulerFactory, so there is nothing to register against — skip cleanly (same guard as
        // CoreScheduledJobsRegistrar).
        if (scope.ServiceProvider.GetService<ISchedulerFactory>() is null)
        {
            logger.LogDebug("Quartz scheduler substrate not present; skipping Scheduler recurring-job registration.");
            return;
        }

        var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();

        foreach (var registration in BuildRegistrations(sweepOptions.Value))
            try
            {
                await scheduler.RegisterRecurringJobAsync(registration, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't let one misconfigured job block the host (or the other registrations). The job
                // simply isn't scheduled and surfaces as absent in the dashboard. JobKey is non-PII.
                logger.LogError(ex, "Failed to register recurring job {JobKey}", registration.JobKey);
            }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}