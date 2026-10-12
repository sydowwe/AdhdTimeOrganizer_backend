using System.Text.Json;
using AdhdTimeOrganizer.Core.domain.serviceContract;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Planning.domain.model.@enum;
using AdhdTimeOrganizer.Planning.domain.service;
using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.action;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.domain.helper;

namespace AdhdTimeOrganizer.Planning.application.service;

/// <summary>
/// The planner's push action buttons (P2-03): <b>Start · Skip</b> on "your block starts in 10 min"
/// (<see cref="NotificationType.PlannedBlockStarting"/>), <b>Done · +N min</b> on "time's up"
/// (<see cref="NotificationType.PlannedBlockOverrun"/>). The same writes the app makes — the status change through
/// <see cref="IPlannerTaskStatusEffects"/>, so the reminder and the done-changed fan-out follow, and the span change as
/// the day view's "wrap up or extend?" makes it (<c>useWrapUpBlock</c>) — with the times taken on <b>the user's clock</b>,
/// which only the server knows reliably; a phone's own zone can differ from the one in the user's settings.
/// <para>
/// Each action applies from one status and is a no-op from the one it leads to, so a retried request changes nothing.
/// From any other status it is <see cref="NotificationActionOutcome.Gone"/>: the user did something else with the
/// block in the meantime, and the app is the place to see what.
/// </para>
/// <list type="bullet">
/// <item><b>Start</b>: not started → in progress, actually started now.</item>
/// <item><b>Skip</b>: not started → cancelled, with no reason (there is no way to give one from a lock screen).</item>
/// <item><b>Done</b>: in progress → completed, actually ended now; its actual start stays, or is the planned one when
/// none was recorded — the rule the day view's one-tap wrap-up follows.</item>
/// <item><b>Extend</b>: an in-progress block whose end has passed ends <c>N</c> minutes from now instead. From now, not
/// from the old end: by the time the push is tapped the end is minutes or more behind, and "+15 min" from it could
/// still be in the past and ring again at once. Once the end is ahead the block already has its time, so a second tap
/// changes nothing. Capped at 23:59 like the day view's, since a span may not wrap past midnight; a block with no room
/// left, or one that already wraps, is <see cref="NotificationActionOutcome.Gone"/>. The overrun nudge re-arms for the
/// new end on its own (<see cref="PlannerTask.OverrunNotifiedAt"/> is read against the current end).</item>
/// </list>
/// </summary>
public class PlannerTaskNotificationActionHandler(
    DbContext dbContext,
    IPlannerTaskStatusEffects statusEffects,
    IReminderRegistrationService reminders,
    IUserTimeZoneResolver timeZones) : INotificationActionHandler, IScopedService
{
    private static readonly TimeOnly LastMinuteOfDay = new(23, 59);

    public bool Handles(NotificationType type, NotificationActionKind kind) => (type, kind) switch
    {
        (NotificationType.PlannedBlockStarting, NotificationActionKind.Start or NotificationActionKind.Skip) => true,
        (NotificationType.PlannedBlockOverrun, NotificationActionKind.Done or NotificationActionKind.Extend) => true,
        _ => false
    };

    public async Task<NotificationActionOutcome> ExecuteAsync(NotificationActionContext context, CancellationToken ct)
    {
        var taskId = context.Type switch
        {
            NotificationType.PlannedBlockStarting => Payload<PlannedBlockStartingPayload>(context.PayloadJson)?.PlannerTaskId,
            NotificationType.PlannedBlockOverrun => Payload<PlannedBlockOverrunPayload>(context.PayloadJson)?.PlannerTaskId,
            _ => null
        };
        if (taskId is not > 0)
            return NotificationActionOutcome.Gone;

        var task = await dbContext.Set<PlannerTask>()
            .Include(t => t.Calendar)
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == context.UserId, ct);
        if (task is null)
            return NotificationActionOutcome.Gone;

        var userNow = WallClockZone.FromUtc(context.Now.UtcDateTime, await timeZones.GetAsync(task.UserId, ct));
        var nowTime = new TimeOnly(userNow.Hour, userNow.Minute);

        return context.Action.Kind switch
        {
            NotificationActionKind.Start => await ChangeStatusAsync(task, from: PlannerTaskStatus.NotStarted, to: PlannerTaskStatus.InProgress,
                t => t.ActualStartTime = nowTime, ct),
            NotificationActionKind.Skip => await ChangeStatusAsync(task, from: PlannerTaskStatus.NotStarted, to: PlannerTaskStatus.Cancelled,
                _ => { }, ct),
            NotificationActionKind.Done => await ChangeStatusAsync(task, from: PlannerTaskStatus.InProgress, to: PlannerTaskStatus.Completed,
                t =>
                {
                    t.ActualStartTime ??= t.StartTime;
                    t.ActualEndTime = nowTime;
                }, ct),
            NotificationActionKind.Extend => await ExtendAsync(task, userNow, context.Action.Minutes, ct),
            _ => NotificationActionOutcome.Gone
        };
    }

    private async Task<NotificationActionOutcome> ChangeStatusAsync(
        PlannerTask task, PlannerTaskStatus from, PlannerTaskStatus to, Action<PlannerTask> stampTimes, CancellationToken ct)
    {
        if (task.Status == to)
            return NotificationActionOutcome.Applied;
        if (task.Status != from)
            return NotificationActionOutcome.Gone;

        // ApplyStatus first: it clears the actual times for Cancelled and the skip reason for everything else, and the
        // stamp then writes the ones this transition records — the order PatchPlannerTaskStatusEndpoint uses.
        task.ApplyStatus(to);
        stampTimes(task);
        await dbContext.SaveChangesAsync(ct);

        await statusEffects.AfterStatusSavedAsync(task, ct);
        return NotificationActionOutcome.Applied;
    }

    private async Task<NotificationActionOutcome> ExtendAsync(PlannerTask task, DateTime userNow, int minutes, CancellationToken ct)
    {
        if (task.Status != PlannerTaskStatus.InProgress || minutes <= 0 || task.EndTime <= task.StartTime)
            return NotificationActionOutcome.Gone;

        if (PlannerTaskMissedRule.EndsAt(task.Calendar.Date, task.StartTime, task.EndTime) > userNow)
            return NotificationActionOutcome.Applied;

        var wanted = new DateTime(userNow.Year, userNow.Month, userNow.Day, userNow.Hour, userNow.Minute, 0).AddMinutes(minutes);
        var newEnd = DateOnly.FromDateTime(wanted) == task.Calendar.Date ? TimeOnly.FromDateTime(wanted) : LastMinuteOfDay;
        if (task.Calendar.Date.ToDateTime(newEnd) <= userNow)
            return NotificationActionOutcome.Gone;

        task.EndTime = newEnd;
        await dbContext.SaveChangesAsync(ct);

        // As PatchPlannerTaskSpanEndpoint: a moved slot moves its reminder. The start did not move, so it is not a
        // postponement and PlannerTaskMoveRecorder has nothing to count.
        await reminders.SyncForPlannerTasksAsync([task.Id], ct);
        return NotificationActionOutcome.Applied;
    }

    /// <summary>The kind's payload record, or null for an empty, malformed or shape-drifted document.</summary>
    private static T? Payload<T>(string payloadJson) where T : class
    {
        try
        {
            return string.IsNullOrWhiteSpace(payloadJson) ? null : JsonHelper.Deserialize<T>(payloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
