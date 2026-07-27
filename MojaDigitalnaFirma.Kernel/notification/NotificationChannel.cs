namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// Delivery channels a notification can go out on. Per-user, per-type opt-out is tracked
/// per channel (see NotificationPreference). FCM/APNs would be added here when native push
/// is introduced — the dispatcher already loops over channels.
/// </summary>
public enum NotificationChannel
{
    /// <summary>Live in-app delivery over SignalR while a client is connected.</summary>
    InApp,

    /// <summary>Background delivery over the Web Push protocol (service worker), even when the app is closed.</summary>
    WebPush,

    /// <summary>
    /// Delivery to the recipient's mailbox over SMTP. The only channel that reaches a user who never
    /// installs the PWA (iOS requires an installed PWA for Web Push). Unlike InApp/WebPush this channel
    /// is NOT default-on for every type — see NotificationChannelDefaults.
    /// </summary>
    Email
}