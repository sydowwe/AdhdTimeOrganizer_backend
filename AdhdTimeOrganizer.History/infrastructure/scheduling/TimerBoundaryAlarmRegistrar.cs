using AdhdTimeOrganizer.History.application.job;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.History.infrastructure.scheduling;

/// <summary>
/// Re-registers every pending timer-boundary alarm on boot, and drops the ones too stale to be worth firing.
/// <para>
/// <b>This class is the reason the feature survives a restart, and it is not optional.</b> The Scheduler runs
/// on Quartz's in-memory RAM job store — a deliberate choice for a single-node monolith, documented in
/// <c>SchedulerQuartzConfig</c> — so <i>every</i> trigger is lost when the process stops, and there is no
/// generic reconciliation that reads the registry rows back. Each owner re-pushes its own schedules on boot.
/// Delete this hosted service and nothing breaks at build time, nothing throws at runtime, and every alarm
/// registered before a deploy silently never fires.
/// </para>
/// <para>
/// Re-registration is safe to do unconditionally: the job key is derived from the row id, so it reschedules in
/// place rather than duplicating, and the Scheduler stamps the fire as scheduled at the boundary instant so
/// its run-log dedup — which survives the restart, unlike the trigger — recognises an already-fired instant
/// and does not fire it twice.
/// </para>
/// </summary>
public sealed class TimerBoundaryAlarmRegistrar(
    IServiceProvider services,
    ILogger<TimerBoundaryAlarmRegistrar> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var alarms = scope.ServiceProvider.GetRequiredService<ITimerBoundaryAlarmService>();

            // Taken BEFORE the read below — see RemoveStrandedJobsAsync for why the order matters.
            var readStartedAt = DateTime.UtcNow;

            // Unscoped on purpose: this runs with no authenticated user, which is exactly when the portal's
            // global user filter (ScopeUserId == null || …) lets every user's rows through. Alarms for all
            // users are re-registered in one pass.
            var pending = await dbContext.Set<TimerBoundaryAlarm>().ToListAsync(cancellationToken);

            var cutoff = DateTime.UtcNow - TimerBoundaryAlarmService.StaleGrace;
            var stale = pending.Where(a => a.BoundaryAt < cutoff).ToList();
            var live = pending.Where(a => a.BoundaryAt >= cutoff).ToList();

            // Dropped rather than fired: a "focus period ended" push for a session the user lived through
            // hours ago is noise, and these rows would otherwise accumulate forever in a table that has no
            // retention job because it is supposed to self-drain.
            if (stale.Count > 0)
                await alarms.CancelAsync(stale, cancellationToken);

            foreach (var alarm in live)
                await alarms.RegisterTriggerAsync(alarm, cancellationToken);

            var stranded = await RemoveStrandedJobsAsync(scope.ServiceProvider, live, readStartedAt, cancellationToken);

            logger.LogInformation(
                "Re-registered {Live} pending timer-boundary alarm(s), dropped {Stale} stale one(s) and removed {Stranded} stranded job(s)",
                live.Count, stale.Count, stranded);
        }
        catch (Exception ex)
        {
            // Never block host startup. A portal with no scheduler substrate wired (or a database not yet
            // migrated on first run) must still boot; the alarms simply do not fire until the next start.
            logger.LogError(ex, "Failed to re-register pending timer-boundary alarms; they will not fire this run");
        }
    }

    /// <summary>
    /// Removes every alarm job whose alarm row is gone.
    /// <para>
    /// <b>The trap this closes.</b> An alarm row can vanish without <see cref="ITimerBoundaryAlarmService.CancelAsync"/>
    /// ever running — it cascades away with its user, and a truncate takes it too. Its Scheduler job is then
    /// stranded: no row means this registrar never re-registers it, the in-memory job store has dropped its
    /// trigger, and it sits <c>Active</c> and overdue for good (scheduler findings B6). The live alarms just
    /// re-registered are the complete set this module owns, so anything else under the handler is stranded.
    /// </para>
    /// <para>
    /// <paramref name="readStartedAt"/> was taken before the alarm rows were read, so a timer started while this
    /// ran — row saved, job registered, both after that instant — is never counted as stranded.
    /// </para>
    /// </summary>
    private async Task<int> RemoveStrandedJobsAsync(
        IServiceProvider scopedServices, IReadOnlyCollection<TimerBoundaryAlarm> live, DateTime readStartedAt, CancellationToken ct)
    {
        try
        {
            var scheduler = scopedServices.GetRequiredService<IScheduler>();
            return await scheduler.RemoveJobsNotInAsync(
                TimerBoundaryAlarmJobHandler.HandlerKey, [.. live.Select(a => a.JobKey)], readStartedAt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The live alarms are already registered; failing here only leaves the stranded rows for next boot.
            logger.LogError(ex, "Failed to remove stranded timer-boundary alarm jobs; they stay Active until the next start");
            return 0;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
