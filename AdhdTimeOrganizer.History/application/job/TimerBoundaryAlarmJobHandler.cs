using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.History.application.service;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;

namespace AdhdTimeOrganizer.History.application.job;

/// <summary>
/// Fires one timer-boundary alarm: read the row, push the notification, delete the row. Invoked by the
/// Scheduler's dispatcher from the one-shot trigger <see cref="TimerBoundaryAlarmService"/> registered when
/// the user pressed Start.
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

        var payload = new TimerBoundaryPayload(
            alarm.Phase,
            activityName,
            alarm.PhaseIndex,
            alarm.PhaseTotal,
            alarm.CycleIndex,
            alarm.CycleTotal,
            alarm.Tag,
            alarm.Url);

        try
        {
            await notificationService.NotifyAsync(NotificationRecipients.User(alarm.UserId), payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deleted below regardless. MaxRetries is 0 for this job precisely because a retry would find no
            // row, and a push about a timer that finished several minutes ago is worse than none at all.
            logger.LogError(ex, "Failed to dispatch the timer-boundary notification for alarm {AlarmId}", alarmId);
        }

        // The table self-drains: one fire, one delete. This is what keeps it free of a retention job.
        // Set-based rather than Remove + SaveChanges, because a cancel landing between the read above and here
        // has already deleted the row, and the tracked delete would throw a concurrency exception for it.
        await dbContext.Set<TimerBoundaryAlarm>()
            .Where(a => a.Id == alarmId)
            .ExecuteDeleteAsync(ct);
    }
}
