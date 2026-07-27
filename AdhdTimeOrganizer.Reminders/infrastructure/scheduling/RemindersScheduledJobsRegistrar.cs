using AdhdTimeOrganizer.Reminders.application.job;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MojaDigitalnaFirma.Kernel.scheduling;

namespace AdhdTimeOrganizer.Reminders.infrastructure.scheduling;

/// <summary>
/// Startup reconciliation seam for the Reminders module: on boot it (re-)registers the module's recurring
/// jobs with the Scheduler module via the <see cref="IScheduler"/> contract — the owner pushes its
/// schedule, Scheduler reconciles. There are exactly two module-wide jobs — the dispatch scan
/// (<see cref="ReminderScanJobHandler"/>) and the ledger-retention purge
/// (<see cref="PurgeExpiredReminderLedgersJobHandler"/>) — never one Quartz trigger per reminder:
/// per-reminder timing lives in the DB (<c>NextOccurrenceAt</c>) and the scan sweeps for what is due.
/// <para>
/// <b>Reminders owns no Quartz.</b> It never calls <c>AddQuartz</c>, owns no Quartz <c>IJob</c>, and references
/// no Quartz type — it registers its scan as a job key + handler key through the Kernel contract. All Quartz
/// infrastructure, the misfire→instruction mapping and the run log live in the Scheduler module.
/// </para>
/// <para>
/// <b>Idempotent:</b> <see cref="IScheduler.RegisterRecurringJobAsync"/> is an upsert by <c>JobKey</c>, so
/// re-running on every boot converges the registry + trigger with no duplicates — and is <i>required</i>, since
/// the Scheduler's RAM job store drops all triggers on restart. <see cref="DisallowConcurrent"/> is requested so
/// two scans never overlap (the scan's check-then-insert dedup is race-free only under that guarantee).
/// </para>
/// <para>
/// Wired by the host DI extension of a deployment that has the Quartz substrate (e.g. HBCleaning). A portal with
/// no scheduler substrate (e.g. the vanilla Sandbox) simply doesn't wire it; the resolve/register is wrapped so a
/// misconfigured cadence or an absent substrate logs and skips rather than crashing the host.
/// </para>
/// </summary>
public sealed class RemindersScheduledJobsRegistrar(
    IServiceProvider services,
    IOptions<ReminderScanOptions> options,
    ILogger<RemindersScheduledJobsRegistrar> logger) : IHostedService
{
    /// <summary>The stable job-key identity of the single recurring scan job (the idempotency key).</summary>
    public const string ScanJobKey = "reminders.scan";

    /// <summary>
    /// The module's second recurring job: its own ledger-retention purge (reminders follow-up 01). Not a
    /// per-reminder trigger either — one monthly sweep over the whole module's history.
    /// </summary>
    public static RecurringJobRegistration RetentionPurgeRegistration { get; } = new()
    {
        JobKey = PurgeExpiredReminderLedgersJobHandler.HandlerKey,
        HandlerKey = PurgeExpiredReminderLedgersJobHandler.HandlerKey,
        OwnerModule = "Reminders",
        // Monthly on the 1st at 03:45 UTC — offset from Core.PurgeExpiredAuditLogs (03:00),
        // Notifications (03:15) and Scheduler.PurgeExpiredRunLogs (03:30) so no two purges contend on one
        // fire instant. Monthly (not daily like Notifications) because the horizon is in years, not days.
        // Idempotent and cheap when nothing has aged out, so a missed run self-heals the next month.
        Schedule = ScheduleSpec.FromCron("0 45 3 1 * ?"),
        DisallowConcurrent = true,
        Description = "GDPR Art. 5(1)(e): purge reminder_dispatch / reminder_occurrence_action rows past retention "
                      + "(keeping each definition's most recent) and long-terminal reminder definitions."
    };

    /// <summary>Build the recurring-job registration the registrar pushes — also the seam tests assert against.</summary>
    public static RecurringJobRegistration BuildRegistration(ReminderScanOptions options) =>
        new()
        {
            JobKey = ScanJobKey,
            HandlerKey = ReminderScanJobHandler.HandlerKey,
            OwnerModule = "Reminders",
            Schedule = options.ToScheduleSpec(),
            MisfirePolicy = options.MisfirePolicy,
            // The scan's dedup (check-then-insert against the dispatch log) is race-free only because no two
            // scans overlap. Single-node + DB-side dedup is the no-double-send guarantee; no persistent store /
            // clustering is pushed onto Scheduler here.
            DisallowConcurrent = true,
            Description = "Reminders dispatch scan: finds due reminder occurrences and dispatches them via the Notification module."
        };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        // Registered independently: a cadence misconfiguration that keeps the scan from registering must
        // not also take down the retention purge, which is a GDPR Art. 5(1)(e) control.
        RecurringJobRegistration[] registrations = [BuildRegistration(options.Value), RetentionPurgeRegistration];

        foreach (var registration in registrations)
            try
            {
                var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
                await scheduler.RegisterRecurringJobAsync(registration, cancellationToken);
            }
            catch (Exception ex)
            {
                // Don't let a misconfigured cadence (or an absent scheduler substrate) block host startup —
                // the job simply isn't scheduled and surfaces as absent in the dashboard. JobKey is non-PII.
                logger.LogError(ex, "Failed to register the reminders recurring job {JobKey}", registration.JobKey);
            }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}