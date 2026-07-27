namespace AdhdTimeOrganizer.Notifications.infrastructure.webPush;

/// <summary>A single device's Web Push subscription, flattened for sending.</summary>
public record PushTarget(string Endpoint, string P256dh, string Auth);

/// <summary>
/// Sends an encrypted Web Push message to one subscription via the browser's push service.
/// Abstracted so the dispatcher doesn't depend on the concrete VAPID library and so a
/// future FCM/APNs sender can sit behind the same shape.
/// </summary>
public interface IWebPushSender
{
    /// <summary>
    /// Delivers <paramref name="payloadJson"/> to <paramref name="target"/>.
    /// Returns true when the push service reports the subscription is gone (HTTP 404/410)
    /// and the caller should prune it; false on success.
    /// Throws for transient/unexpected failures.
    /// </summary>
    Task<bool> SendAsync(PushTarget target, string payloadJson, CancellationToken ct = default);
}