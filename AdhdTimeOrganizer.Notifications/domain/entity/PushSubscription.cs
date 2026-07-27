using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.entity.user;

namespace AdhdTimeOrganizer.Notifications.domain.entity;

/// <summary>
/// A single browser/PWA Web Push subscription owned by a user. One user has many (one per
/// device/browser). Created when the frontend calls PushManager.subscribe() and POSTs the
/// result. [NoAudit] — contains opaque endpoint/keys, not business data.
/// </summary>
[NoAudit]
public class PushSubscription : BaseTableEntity, IEntityWithUser
{
    /// <summary>Owner. Plain indexed column; the FK is configured host-side (see Notification.UserId).</summary>
    public long UserId { get; set; }

    /// <summary>Push service endpoint URL returned by the browser.</summary>
    public required string Endpoint { get; set; }

    /// <summary>Client public key (base64url) from subscription.getKey("p256dh").</summary>
    public required string P256dh { get; set; }

    /// <summary>Auth secret (base64url) from subscription.getKey("auth").</summary>
    public required string Auth { get; set; }

    public string? UserAgent { get; set; }
}