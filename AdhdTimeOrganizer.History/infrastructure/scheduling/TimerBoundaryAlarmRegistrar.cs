using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

            // Unscoped on purpose: this runs with no authenticated user, which is exactly when the portal's
            // global user filter (ScopeUserId == null || …) lets every user's rows through. Alarms for all
            // users are re-registered in one pass.
            var pending = await dbContext.Set<TimerBoundaryAlarm>().ToListAsync(cancellationToken);
            if (pending.Count == 0)
                return;

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

            logger.LogInformation(
                "Re-registered {Live} pending timer-boundary alarm(s) and dropped {Stale} stale one(s)",
                live.Count, stale.Count);
        }
        catch (Exception ex)
        {
            // Never block host startup. A portal with no scheduler substrate wired (or a database not yet
            // migrated on first run) must still boot; the alarms simply do not fire until the next start.
            logger.LogError(ex, "Failed to re-register pending timer-boundary alarms; they will not fire this run");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
