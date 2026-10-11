using AdhdTimeOrganizer.Planning.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.Planning.application.service;

/// <summary>
/// Maps the planner's sweep findings onto the <c>Sydowwe.Framework.Contracts</c> notification contract — the
/// counterpart of Routines' <c>RoutinePeriodNotificationService</c>. The recipient is always the block's own user;
/// channels, quiet hours and the away hold are applied downstream by the Notifications module.
/// </summary>
public class PlannerNotificationService(
    INotificationService notificationService,
    ILogger<PlannerNotificationService> logger) : IPlannerNotificationService, IScopedService
{
    public Task<bool> NotifyBlocksMissedAsync(long userId, DateOnly date, int count, long firstTaskId, CancellationToken ct = default) =>
        count <= 0
            ? Task.FromResult(false)
            : SafeNotifyAsync(userId, new PlannedBlocksMissedPayload(date, count, firstTaskId), ct);

    public Task<bool> NotifyBlockOverrunAsync(long userId, long plannerTaskId, string? activityName, int minutesOver, CancellationToken ct = default) =>
        SafeNotifyAsync(userId, new PlannedBlockOverrunPayload(plannerTaskId, activityName, Math.Max(0, minutesOver)), ct);

    /// <summary>
    /// Best-effort dispatch: a sweep must carry on to the next user when SignalR or SMTP has a bad minute. Logs ids
    /// only — the activity name is the user's own words and stays out of logs.
    /// </summary>
    private async Task<bool> SafeNotifyAsync<TPayload>(long userId, TPayload payload, CancellationToken ct)
        where TPayload : INotificationPayload
    {
        try
        {
            await notificationService.NotifyAsync(NotificationRecipients.User(userId), payload, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Planner notification {Payload} failed for user {UserId}", typeof(TPayload).Name, userId);
            return false;
        }
    }
}
