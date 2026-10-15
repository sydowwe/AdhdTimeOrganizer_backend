using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using AdhdTimeOrganizer.History.domain.model.@enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.History.application.job;

/// <summary>
/// Fires one timer alarm: read the row, push the notification, delete the row. Invoked by the Scheduler's
/// dispatcher from the one-shot trigger <see cref="TimerBoundaryAlarmService"/> registered when the user pressed
/// Start. A row is either a phase boundary (<see cref="NotificationType.TimerBoundary"/>) or a running stopwatch's
/// check-in (<see cref="NotificationType.StopwatchCheckIn"/>); see <see cref="TimerAlarmKind"/>.
/// <para>
/// <b>Background context — no authenticated user.</b> That is what makes the read below work: the portal's
/// global user-scoping filter is written <c>ScopeUserId == null || e.UserId == ScopeUserId</c>, so an
/// unauthenticated context sees every row rather than none. The recipient therefore comes off the row's own
/// <c>UserId</c> and never from an ambient principal.
/// </para>
/// </summary>
public class TimerBoundaryAlarmJobHandler(
    DbContext dbContext,
    INotificationService notificationService,
    IQuietHoursReader quietHours,
    TimeProvider timeProvider,
    ILogger<TimerBoundaryAlarmJobHandler> logger) : IScheduledJobHandler, IScopedService
{
    public const string HandlerKey = "History.TimerBoundaryAlarm";

    public string Key => HandlerKey;

    public async Task ExecuteAsync(ScheduledJobContext context, CancellationToken ct)
    {
        if (context.GetPayload<TimerBoundaryAlarmJobPayload>() is not { AlarmId: var alarmId })
        {
            logger.LogError("Timer-boundary job {JobKey} carried no alarm id; nothing to fire", context.JobKey);
            return;
        }

        var alarm = await dbContext.Set<TimerBoundaryAlarm>().FirstOrDefaultAsync(a => a.Id == alarmId, ct);
        if (alarm is null)
        {
            // The ordinary outcome of a cancel that raced the fire — Stop deleted the row while the trigger
            // was already on its way. Not a failure: silence is exactly what a cancelled alarm should produce.
            logger.LogDebug("Timer alarm {AlarmId} no longer exists; it was cancelled before firing", alarmId);
            return;
        }

        if (alarm.Kind == TimerAlarmKind.StopwatchCheckIn && await IsInsideQuietHoursAsync(alarm.UserId, ct))
        {
            // Skipped, not deferred. The dispatcher would hold the push to the end of the window, and "still on
            // X?" at 07:00 about a stopwatch from 01:00 is noise — so is its bell row, so NotifyAsync is never
            // called. The next check-in, an hour on, asks again if the window is over by then.
            logger.LogDebug("Stopwatch check-in {AlarmId} fell inside its recipient's quiet hours; skipped", alarmId);
        }
        else
        {
            try
            {
                await notificationService.NotifyAsync(
                    NotificationRecipients.User(alarm.UserId), await BuildPayloadAsync(alarm, ct), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Deleted below regardless. MaxRetries is 0 for this job precisely because a retry would find
                // no row, and a push about a timer that finished several minutes ago is worse than none at all.
                logger.LogError(ex, "Failed to dispatch the timer-boundary notification for alarm {AlarmId}", alarmId);
            }
        }

        // The table self-drains: one fire, one delete. This is what keeps it free of a retention job.
        // Set-based rather than Remove + SaveChanges, because a cancel landing between the read above and here
        // has already deleted the row, and the tracked delete would throw a concurrency exception for it.
        await dbContext.Set<TimerBoundaryAlarm>()
            .Where(a => a.Id == alarmId)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<INotificationPayload> BuildPayloadAsync(TimerBoundaryAlarm alarm, CancellationToken ct)
    {
        // Resolved at fire time rather than snapshotted at registration, so a renamed activity reads
        // correctly. Null (deleted activity, or none named) renders the name-less body — never throws.
        // Scoped to the alarm's own UserId, not left to the global filter: this context is unauthenticated,
        // so that filter passes every row. The schedule endpoint already proves ownership, and this keeps a
        // foreign name unreachable even if a row with someone else's activity id ever reaches the table.
        var activityName = alarm.ActivityId is { } activityId
            ? await dbContext.Set<Activity>()
                .Where(a => a.Id == activityId && a.UserId == alarm.UserId)
                .Select(a => (string?)a.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        return alarm.Kind switch
        {
            // ElapsedMinutes is required by the validator; 0 on a bad row renders the name-less body.
            TimerAlarmKind.StopwatchCheckIn => new StopwatchCheckInPayload(
                alarm.ElapsedMinutes ?? 0,
                activityName,
                alarm.Tag,
                alarm.Url),

            // Phase is required by the validator for a boundary; a row without one rings as a plain countdown.
            _ => new TimerBoundaryPayload(
                alarm.Phase ?? TimerBoundaryPhase.Plain,
                activityName,
                alarm.PhaseIndex,
                alarm.PhaseTotal,
                alarm.CycleIndex,
                alarm.CycleTotal,
                alarm.Tag,
                alarm.Url)
        };
    }

    /// <summary>
    /// Whether the recipient is inside their quiet hours right now. Asked at fire time, not at registration: the
    /// window can be edited, or follow a sleep schedule, between Start and the check-in. A failed read counts as
    /// "not quiet" — the dispatcher still defers the push in that case, so the user is not woken either way.
    /// </summary>
    private async Task<bool> IsInsideQuietHoursAsync(long userId, CancellationToken ct)
    {
        try
        {
            var quiet = await quietHours.GetQuietUntilAsync([userId], timeProvider.GetUtcNow(), ct);
            return quiet.ContainsKey(userId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read quiet hours for stopwatch check-in recipient {UserId}; sending it", userId);
            return false;
        }
    }
}
