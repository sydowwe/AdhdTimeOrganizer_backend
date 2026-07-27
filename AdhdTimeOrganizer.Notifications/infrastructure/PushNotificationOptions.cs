namespace AdhdTimeOrganizer.Notifications.infrastructure;

/// <summary>
/// VAPID credentials for Web Push. Bound from the "PushNotification" config section.
/// The private key is a secret — keep it out of appsettings.json (use .env / user-secrets
/// in dev, a secret store in prod). See setup.md.
/// </summary>
public class PushNotificationOptions
{
    public const string SectionName = "PushNotification";

    /// <summary>VAPID "sub" claim — a mailto: or https: contact, e.g. "mailto:admin@example.com".</summary>
    public string VapidSubject { get; set; } = string.Empty;

    /// <summary>VAPID public key (base64url). Also handed to the frontend for PushManager.subscribe().</summary>
    public string VapidPublicKey { get; set; } = string.Empty;

    /// <summary>VAPID private key (base64url). SECRET.</summary>
    public string VapidPrivateKey { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(VapidPublicKey) && !string.IsNullOrWhiteSpace(VapidPrivateKey);
}