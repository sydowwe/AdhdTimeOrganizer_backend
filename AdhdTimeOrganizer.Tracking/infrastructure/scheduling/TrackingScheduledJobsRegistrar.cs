using AdhdTimeOrganizer.Tracking.infrastructure.jobs;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Tracking.infrastructure.scheduling;

/// <summary>
/// Boot reconciliation for the Tracking slice: pushes its retention purge onto the Scheduler module through
/// the <see cref="IScheduler"/> contract. The slice owns the retention rule and the cadence; Scheduler owns
/// the trigger, the run log, retries and failure alerting — which is why this slice references no Quartz.
/// <para>
/// <b>Required on every boot:</b> the registration is an idempotent upsert by <c>JobKey</c>, and the
/// Scheduler's RAM job store drops all triggers on restart. This one carries a GDPR Art. 5(1)(e) control,
/// so a failure to register is logged as an error rather than swallowed quietly.
/// </para>
/// </summary>
public sealed class TrackingScheduledJobsRegistrar(
    IServiceProvider services,
    ILogger<TrackingScheduledJobsRegistrar> logger) : IHostedService
{
    public static RecurringJobRegistration RetentionPurgeRegistration { get; } = new()
    {
        JobKey = PurgeExpiredActivityTrackingEntriesJobHandler.HandlerKey,
        HandlerKey = PurgeExpiredActivityTrackingEntriesJobHandler.HandlerKey,
        OwnerModule = "Tracking",
        // Was a host-side Quartz trigger with cron "0 30 3 * * ?" (UTC); kept identical. Note the offsets the
        // other purges already occupy — Notifications 03:15, Scheduler 03:30 monthly, Reminders 03:45 monthly.
        Schedule = ScheduleSpec.FromCron("0 30 3 * * ?"),
        DisallowConcurrent = true,
        Description = "GDPR Art. 5(1)(e): purge desktop_activity_entry / web_extension_activity_entry rows past retention."
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        try
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
            await scheduler.RegisterRecurringJobAsync(RetentionPurgeRegistration, cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't block host startup — the purge simply isn't scheduled and shows as absent in the
            // dashboard. JobKey is non-PII.
            logger.LogError(ex, "Failed to register the tracking recurring job {JobKey}", RetentionPurgeRegistration.JobKey);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
