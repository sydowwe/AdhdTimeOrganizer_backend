using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;

namespace AdhdTimeOrganizer.application.service.morningBriefing;

/// <summary>Raises the <see cref="NotificationType.MorningBriefing"/> notification — the one class that does.</summary>
public interface IMorningBriefingNotifier
{
    /// <summary>
    /// Best-effort dispatch to the briefing's one recipient. False when the dispatch was swallowed, so the sweep
    /// leaves the day open and a later run that morning retries rather than marking a briefing that never went out.
    /// </summary>
    Task<bool> NotifyAsync(long userId, MorningBriefingPayload payload, CancellationToken ct);
}

/// <inheritdoc cref="IMorningBriefingNotifier"/>
public class MorningBriefingNotifier(
    INotificationService notificationService,
    ILogger<MorningBriefingNotifier> logger) : IMorningBriefingNotifier, IScopedService
{
    public async Task<bool> NotifyAsync(long userId, MorningBriefingPayload payload, CancellationToken ct)
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
            // The payload is counts only, but the user id is all a log line needs.
            logger.LogWarning(ex, "Morning briefing failed for user {UserId}", userId);
            return false;
        }
    }
}
