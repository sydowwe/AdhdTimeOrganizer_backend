using AdhdTimeOrganizer.Tracking.application.notification;
using AdhdTimeOrganizer.Tracking.domain.helper.unified;
using AdhdTimeOrganizer.Tracking.domain.serviceContract;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.Tracking.application.service;

/// <summary>
/// P2-04: maps a silent tracker onto <see cref="TrackerSilentPayload"/>. The source travels as its wire name
/// (<see cref="TrackingSourceNames"/>), the same string the client already reads on the unified dashboards, and the
/// host's notification texts word it. The recipient is always the tracker's own user.
/// </summary>
public class TrackerSilenceNotifier(
    INotificationService notificationService,
    ILogger<TrackerSilenceNotifier> logger) : ITrackerSilenceNotifier, IScopedService
{
    public async Task<bool> NotifyAsync(long userId, TrackingSource source, DateTime lastSeenAt, CancellationToken ct)
    {
        try
        {
            await notificationService.NotifyAsync(
                NotificationRecipients.User(userId),
                new TrackerSilentPayload(source.ToWireName(), lastSeenAt),
                ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tracker-silent notification for the {Source} tracker failed for user {UserId}", source, userId);
            return false;
        }
    }
}
