using AdhdTimeOrganizer.Notifications.application.job;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.Notifications.infrastructure.scheduling;

/// <summary>
/// Startup reconciliation seam for the Notifications module's recurring jobs — currently just its
/// retention purge (<see cref="PurgeExpiredNotificationHistoryJobHandler"/>). Mirrors
/// <c>CoreScheduledJobsRegistrar</c>: on boot it (re-)registers each owned job via the
/// <see cref="IScheduler"/> contract; <see cref="IScheduler.RegisterRecurringJobAsync"/> is an idempotent
/// upsert by <c>JobKey</c>, so re-running on every boot converges the registry + Quartz triggers with no
/// duplicates (and is <i>required</i>, since the RAM job store drops all triggers on restart).
/// <para>
/// Wired once in the shared <c>AddCore</c>, so both the vanilla Sandbox and the HBCleaning host get it.
/// Quartz itself belongs to the Scheduler module — this module depends only on <c>Sydowwe.Framework.Contracts</c> + Framework and
/// reaches the substrate through the <see cref="IScheduler"/> contract, so a host that never wires
/// Quartz simply fails to register (logged, never fatal). See <c>StartAsync</c>.
/// </para>
/// </summary>
public sealed class NotificationsScheduledJobsRegistrar(
    IServiceProvider services,
    ILogger<NotificationsScheduledJobsRegistrar> logger) : IHostedService
{
    /// <summary>The recurring jobs owned by the Notifications module, registered on every boot.</summary>
    public static IReadOnlyList<RecurringJobRegistration> Registrations { get; } =
    [
        new()
        {
            JobKey = PurgeExpiredNotificationHistoryJobHandler.HandlerKey,
            HandlerKey = PurgeExpiredNotificationHistoryJobHandler.HandlerKey,
            OwnerModule = "Notifications",
            // Daily at 03:15 UTC — offset from Core.PurgeExpiredAuditLogs (03:00) and
            // Scheduler.PurgeExpiredRunLogs (03:30) so the three purges never contend on one fire
            // instant. Daily (not monthly) because notification volume is high and the windows are in
            // days: a monthly cadence would let up to a month of expired payload PII linger. Idempotent
            // and cheap when nothing has aged out, so a missed run self-heals the next day.
            Schedule = ScheduleSpec.FromCron("0 15 3 * * ?"),
            DisallowConcurrent = true,
            Description = "GDPR Art. 5(1)(e): purge read notifications past 90d, all notifications past 365d, and push subscriptions idle past 180d."
        },
        new()
        {
            JobKey = FlushDeferredNotificationsJobHandler.HandlerKey,
            HandlerKey = FlushDeferredNotificationsJobHandler.HandlerKey,
            OwnerModule = "Notifications",
            // Every 15 minutes: the delivery lag a user sees after their quiet-hours window ends. Deliberately
            // coarse — these are the notifications we decided were not urgent enough to buzz a phone at night,
            // and a finer cadence would only trade a mostly-empty query for a shorter tail. The sweep is a
            // filtered-index seek and a no-op when nothing is due.
            Schedule = ScheduleSpec.FromCron("0 0/15 * * * ?"),
            DisallowConcurrent = true,
            Description = "Deliver Web Push / Email withheld during a recipient's quiet hours, once their window has ended."
        }
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();

        // Resolution and registration share one try/catch, matching RemindersScheduledJobsRegistrar —
        // the satellite-module shape. This module references only Sydowwe.Framework.Contracts + Framework, so it cannot test
        // `GetService<ISchedulerFactory>() is null` the way CoreScheduledJobsRegistrar does; that guard
        // is available to Core only because Core already carries the Quartz package. Quartz belongs to
        // the Scheduler module, and modules reach it through the Sydowwe.Framework.Contracts IScheduler contract.
        //
        // Deliberately logged at Error, not swallowed at Debug: on a Quartz-less host (the vanilla
        // Sandbox) resolving IScheduler throws, because AddCore's factory resolves ISchedulerFactory
        // eagerly — but a genuinely misconfigured scheduler throws exactly the same way. Since this job
        // is a GDPR Art. 5(1)(e) control, an unregistered purge must be visible in the log rather than
        // silently indistinguishable from an expected no-op. Never rethrow: a scheduling problem must
        // not block host startup, and the job surfaces as absent in the scheduler dashboard.
        foreach (var registration in Registrations)
            try
            {
                var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();
                await scheduler.RegisterRecurringJobAsync(registration, cancellationToken);
            }
            catch (Exception ex)
            {
                // JobKey is non-PII.
                logger.LogError(ex, "Failed to register recurring job {JobKey}", registration.JobKey);
            }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}