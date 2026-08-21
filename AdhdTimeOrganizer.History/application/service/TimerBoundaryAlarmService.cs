using AdhdTimeOrganizer.History.application.job;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.serviceContract;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.History.application.service;

/// <summary>
/// Turns a timer session's phase boundaries into pending <see cref="TimerBoundaryAlarm"/> rows plus one
/// one-shot Scheduler trigger each, and tears both down again on cancel. The only class that knows the job-key
/// format or the row-to-trigger mapping; endpoints and the boot registrar stay one-liners over it.
/// <para>
/// <b>Why the Scheduler and not the Reminders module.</b> Reminders has no per-reminder trigger by design —
/// every reminder fires from one recurring sweep whose cadence (<c>ReminderScanOptions.CadenceMinutes</c>,
/// default 5) is documented as the firing-precision floor. Five minutes is ample for "probation ends in 30
/// days" and useless for a pomodoro. <see cref="ScheduleSpec.RunOnceAt"/> is a real trigger on the exact
/// instant, which is the whole reason this feature exists. The cost is one live trigger per boundary — the
/// thing Reminders refuses to do — which is fine at this scale and would not be at thousands of concurrent
/// timers.
/// </para>
/// </summary>
public class TimerBoundaryAlarmService(
    DbContext dbContext,
    IScheduler scheduler,
    ILogger<TimerBoundaryAlarmService> logger) : ITimerBoundaryAlarmService, IScopedService
{
    /// <summary>Owning module label for the Scheduler dashboard. Matches the slice, not the host.</summary>
    public const string OwnerModule = "History";

    /// <summary>
    /// How far past its instant an alarm may still be fired on re-registration. Inside the window a boundary
    /// missed because the process was down still rings (the laptop-woke-up case, which is the point); outside
    /// it the row is dropped, because a "focus period ended" push for a session that ended yesterday is noise.
    /// </summary>
    public static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Replaces the whole set of alarms for one session: cancels anything already registered under
    /// <paramref name="sessionId"/>, then registers <paramref name="boundaries"/>.
    /// <para>
    /// <b>Replace, never merge</b> — that is what makes a re-POST after a reload idempotent rather than a
    /// conflict, and it is why the client may re-send a set it is unsure about. Boundaries already in the past
    /// are dropped here rather than registered: <c>RunOnceAt</c> would fire a past-due instant immediately via
    /// the misfire path, which for a boundary the user already lived through is a spurious alarm.
    /// </para>
    /// </summary>
    public async Task<int> ReplaceSessionAsync(
        long userId, Guid sessionId, IReadOnlyCollection<TimerBoundaryAlarm> boundaries, CancellationToken ct = default)
    {
        await CancelSessionAsync(userId, sessionId, ct);

        var cutoff = DateTime.UtcNow;
        var pending = boundaries.Where(b => b.BoundaryAt > cutoff).ToList();
        if (pending.Count == 0)
            return 0;

        foreach (var alarm in pending)
        {
            alarm.UserId = userId;
            alarm.SessionId = sessionId;
            alarm.BoundaryAt = DateTime.SpecifyKind(alarm.BoundaryAt, DateTimeKind.Utc);
        }

        await dbContext.AddRangeAsync(pending, ct);

        // Saved before any trigger is registered, because the job key is derived from the row id and the
        // handler's only input is that id. A trigger registered against an unsaved row would fire at nothing.
        await dbContext.SaveChangesAsync(ct);

        foreach (var alarm in pending)
            await RegisterTriggerAsync(alarm, ct);

        return pending.Count;
    }

    /// <summary>
    /// Cancels every alarm for one session: removes the triggers, then deletes the rows.
    /// <para>
    /// <b>A no-op on an unknown session, never a 404.</b> A partially-registered set is the normal outcome of a
    /// tab dying mid-flight, and the client cancels the whole set regardless of what it thinks landed.
    /// </para>
    /// </summary>
    public async Task CancelSessionAsync(long userId, Guid sessionId, CancellationToken ct = default)
    {
        var alarms = await dbContext.Set<TimerBoundaryAlarm>()
            .Where(a => a.UserId == userId && a.SessionId == sessionId)
            .ToListAsync(ct);

        await CancelAsync(alarms, ct);
    }

    /// <summary>
    /// Removes the triggers for <paramref name="alarms"/> and deletes the rows. Shared by session cancel and
    /// the boot registrar's stale sweep.
    /// </summary>
    public async Task CancelAsync(IReadOnlyCollection<TimerBoundaryAlarm> alarms, CancellationToken ct = default)
    {
        if (alarms.Count == 0)
            return;

        foreach (var alarm in alarms)
            try
            {
                // Idempotent no-op on an unknown key, so a row whose trigger was already lost to a restart
                // (the job store is in-memory) unregisters cleanly rather than throwing on the way out.
                await scheduler.RemoveJobAsync(alarm.JobKey, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The row is about to be deleted either way, and the handler no-ops on a missing row — so a
                // stranded trigger is harmless. Logged because a pattern of these means the substrate is down.
                logger.LogWarning(ex, "Failed to remove the timer-alarm trigger {JobKey}; its row is being deleted anyway", alarm.JobKey);
            }

        dbContext.RemoveRange(alarms);
        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Registers one alarm's one-shot trigger. Idempotent by job key, so the boot registrar re-registering an
    /// alarm reschedules in place; and because the Scheduler stamps the fire as scheduled at
    /// <c>RunAtUtc</c>, its run-log dedup (which survives a restart, unlike the in-memory trigger) recognises a
    /// boot re-registration of an already-fired instant as a duplicate and does not fire it twice.
    /// </summary>
    public async Task RegisterTriggerAsync(TimerBoundaryAlarm alarm, CancellationToken ct = default)
    {
        try
        {
            await scheduler.RegisterRecurringJobAsync(new RecurringJobRegistration
            {
                JobKey = alarm.JobKey,
                HandlerKey = TimerBoundaryAlarmJobHandler.HandlerKey,
                OwnerModule = OwnerModule,
                Schedule = ScheduleSpec.RunOnceAt(alarm.BoundaryAt),

                // The alarm's own row id, and nothing else: the payload is persisted verbatim and logged on
                // failure, so everything the notification needs is re-fetched by the handler instead.
                Payload = new TimerBoundaryAlarmJobPayload(alarm.Id),

                // A missed fire must still ring — the process being down over the boundary is exactly the
                // case this feature exists for. FireAndProceed catches up on recovery; the run-log dedup
                // keeps that at-most-once.
                MisfirePolicy = MisfirePolicy.FireAndProceed,

                // One row, one send, then the row is deleted. A retry would have nothing left to read, and a
                // late duplicate push about a finished timer is worse than a dropped one.
                MaxRetries = 0,

                // A user's timer alarm failing is not something an administrator needs woken for; the run log
                // still records it.
                AlertOnFailure = false,
                Description = "One-shot end-of-phase alarm for a running timer."
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Don't fail the caller's request: the row is committed, so the boot registrar will pick this
            // alarm up on the next start. The user loses the alarm for this session only.
            logger.LogError(ex,
                "Failed to register the one-shot trigger {JobKey} for timer alarm {AlarmId}; the alarm will not fire this run",
                alarm.JobKey, alarm.Id);
        }
    }
}

/// <summary>
/// The Scheduler payload for one alarm — the row id alone. Everything else is re-read by the handler, per the
/// payload's PII rule (it is persisted verbatim and logged on failure).
/// </summary>
public sealed record TimerBoundaryAlarmJobPayload(long AlarmId);
