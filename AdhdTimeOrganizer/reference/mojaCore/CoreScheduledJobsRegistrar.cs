using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MojaDigitalnaFirma.Core.application.job;
using MojaDigitalnaFirma.Kernel.scheduling;
using Quartz;
using IScheduler = MojaDigitalnaFirma.Kernel.scheduling.IScheduler;

namespace MojaDigitalnaFirma.Core.infrastructure.scheduling;

/// <summary>
/// Startup reconciliation seam for the vanilla Core module's recurring jobs (Scheduler phase 05). On boot
/// it (re-)registers each owned job with the Scheduler via the <see cref="Kernel.scheduling.IScheduler"/> contract — the
/// owning module pushes its schedule; Scheduler reconciles. <see cref="Kernel.scheduling.IScheduler.RegisterRecurringJobAsync"/>
/// is an idempotent upsert by <c>JobKey</c>, so re-running on every boot converges the registry + Quartz
/// triggers with no duplicates (and is <i>required</i>, since the RAM job store drops all triggers on restart).
/// <para>
/// This is the owner-side hook the recipe calls for: an <see cref="IHostedService"/> in the owning module,
/// wired by the host via <c>AddCore</c> — never a Scheduler-side enumerator. Add a module's further migrated
/// jobs to <see cref="Registrations"/>.
/// </para>
/// </summary>
public sealed class CoreScheduledJobsRegistrar(
    IServiceProvider services,
    ILogger<CoreScheduledJobsRegistrar> logger) : IHostedService
{
    /// <summary>The recurring jobs owned by the vanilla Core module, registered on every boot.</summary>
    public static IReadOnlyList<RecurringJobRegistration> Registrations { get; } =
    [
        new RecurringJobRegistration
        {
            JobKey = PurgeExpiredAuditLogsJobHandler.HandlerKey,
            HandlerKey = PurgeExpiredAuditLogsJobHandler.HandlerKey,
            OwnerModule = "Core",
            // Monthly on the 1st at 03:00 UTC — unchanged from the legacy Quartz trigger. Idempotent and a
            // no-op until a partition fully ages out, so a missed run self-heals the next month.
            Schedule = ScheduleSpec.FromCron("0 0 3 1 * ?"),
            // Mirrors the legacy [DisallowConcurrentExecution] on the job.
            DisallowConcurrent = true,
            Description = "GDPR Art. 5(1)(e): drop expired audit_log partitions (3y) + purge business_audit_log rows (10y).",
        },
        new RecurringJobRegistration
        {
            JobKey = PurgeExpiredSerilogLogsJobHandler.HandlerKey,
            HandlerKey = PurgeExpiredSerilogLogsJobHandler.HandlerKey,
            OwnerModule = "Core",
            // Daily at 03:15 UTC — unchanged from the legacy Quartz trigger ("0 15 3 * * ?", InTimeZone(Utc);
            // TimeZoneId null ⇒ UTC, so the fire instant is preserved exactly). Pure maintenance with nothing
            // wall-clock-anchored, so local time would be meaningless here.
            Schedule = ScheduleSpec.FromCron("0 15 3 * * ?"),
            // Mirrors the legacy [DisallowConcurrentExecution] on the job.
            DisallowConcurrent = true,
            // Behaviour-preserving: the legacy raw IJob had no retry, and none is wanted. A plain row delete
            // that fails self-heals on the next nightly run — tomorrow simply purges one extra day — so a
            // retry storm would buy nothing over waiting 24h.
            MaxRetries = 0,
            // Deliberately left ON (the contract default): with MaxRetries = 0 every failure is terminal, and a
            // silently failing purge means log rows (which may carry free-text PII) sit past their 30d horizon
            // indefinitely with nobody told. That is exactly a failure an unattended owner needs to hear about.
            AlertOnFailure = true,
            Description = "GDPR Art. 5(1)(e): purge serilog_logs rows past the 30d retention horizon (no-op where the Postgres sink is disabled).",
        },
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        // The Quartz/Scheduler substrate is a host-level opt-in: a portal that doesn't wire it (e.g. the
        // vanilla Sandbox) has no ISchedulerFactory, so there is nothing to register against. Skip cleanly
        // there — this preserves the legacy behaviour (these jobs were only ever scheduled in the HBCleaning
        // host's AddQuartz block) and keeps the shared AddCore safe across portals with and without it.
        if (scope.ServiceProvider.GetService<ISchedulerFactory>() is null)
        {
            logger.LogDebug("Quartz scheduler substrate not present; skipping Core recurring-job registration.");
            return;
        }

        var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();

        foreach (var registration in Registrations)
        {
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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}