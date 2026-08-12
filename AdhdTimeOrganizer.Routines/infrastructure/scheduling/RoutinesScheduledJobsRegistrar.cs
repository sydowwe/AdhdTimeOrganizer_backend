using AdhdTimeOrganizer.Routines.infrastructure.jobs;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Routines.infrastructure.scheduling;

/// <summary>
/// Boot reconciliation for the Routines slice: pushes its two recurring jobs onto the Scheduler module
/// through the <see cref="IScheduler"/> contract, the same way the Notifications / Reminders / Scheduler
/// modules do. The slice owns the work (two <see cref="IScheduledJobHandler"/>s) and the cadence; Scheduler
/// owns the trigger, the run log, retries and failure alerting — which is why this slice references no Quartz.
/// <para>
/// <b>Required on every boot:</b> <see cref="IScheduler.RegisterRecurringJobAsync"/> is an idempotent upsert
/// by <c>JobKey</c>, and the Scheduler's RAM job store drops all triggers on restart.
/// </para>
/// </summary>
public sealed class RoutinesScheduledJobsRegistrar(
    IServiceProvider services,
    ILogger<RoutinesScheduledJobsRegistrar> logger) : IHostedService
{
    /// <summary>Daily 02:00 reset of the routine to-do lists — addressed to the database, not to a person.</summary>
    public static RecurringJobRegistration ResetRegistration { get; } = new()
    {
        JobKey = RoutineTodoListResetJobHandler.HandlerKey,
        HandlerKey = RoutineTodoListResetJobHandler.HandlerKey,
        OwnerModule = "Routines",
        // Was a host-side Quartz trigger with cron "0 0 2 * * ?" (UTC, since the old block set no time zone);
        // kept identical so the extraction does not move anyone's reset instant.
        Schedule = ScheduleSpec.FromCron("0 0 2 * * ?"),
        DisallowConcurrent = true,
        Description = "Resets routine to-do lists whose period has rolled over and records the period completion."
    };

    /// <summary>
    /// Daily 09:00 nudge sweep. Deliberately not alongside the reset: this one is read by a person who is
    /// expected to act on it.
    /// </summary>
    public static RecurringJobRegistration NudgeRegistration { get; } = new()
    {
        JobKey = RoutinePeriodNudgeJobHandler.HandlerKey,
        HandlerKey = RoutinePeriodNudgeJobHandler.HandlerKey,
        OwnerModule = "Routines",
        Schedule = ScheduleSpec.FromCron("0 0 9 * * ?"),
        DisallowConcurrent = true,
        Description = "Sends the lead-time 'period ending soon' nudges and the streak grace-expiry warnings."
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        // Registered independently: one job's cadence problem must not keep the other off the schedule.
        RecurringJobRegistration[] registrations = [ResetRegistration, NudgeRegistration];

        foreach (var registration in registrations)
            try
            {
                var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
                await scheduler.RegisterRecurringJobAsync(registration, cancellationToken);
            }
            catch (Exception ex)
            {
                // Never block host startup on a scheduling problem — the job simply isn't scheduled and shows
                // as absent in the dashboard. JobKey is non-PII.
                logger.LogError(ex, "Failed to register the routines recurring job {JobKey}", registration.JobKey);
            }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
