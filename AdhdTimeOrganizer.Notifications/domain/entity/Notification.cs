using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;

namespace AdhdTimeOrganizer.Notifications.domain.entity;

/// <summary>
/// Persisted notification history / bell content. UserId is the *recipient*, not the acting
/// user — must not be auto-stamped from the logged-in principal, so this does not inherit
/// BaseEntityWithUser. Marked [NoAudit] — high volume; semantic events use IAuditService.
/// </summary>
[NoAudit]
public class Notification : BaseTableEntity
{
    /// <summary>
    /// Recipient. A plain indexed column with no navigation property: the module deliberately does not
    /// know the host's concrete user type. The FK is configured host-side by the composition root.
    /// </summary>
    public long UserId { get; set; }

    public NotificationType Type { get; set; }

    /// <summary>
    /// JSON payload (jsonb). Title/body are rendered from Type + this at read/send time.
    /// <para>
    /// Bound by the payload PII contract — <b>the rule and the reasoning live on
    /// <see cref="INotificationPayload"/></b>, stated once for
    /// all three modules that persist a payload. Enforced structurally: what lands here is always the
    /// serialization of a typed payload record, and a guard test rejects person-data fields on those records.
    /// </para>
    /// </summary>
    public string PayloadJson { get; set; } = "{}";

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// Set when the recipient was inside their <see cref="NotificationQuietHours"/> window at send time: the
    /// instant that window ends, i.e. the earliest moment the withheld <b>background</b> channels (Web Push,
    /// Email) may go out. <c>null</c> = nothing withheld, the normal case.
    /// <para>
    /// Only background delivery is late. This row itself was written immediately and the InApp (SignalR) push
    /// already fired, so the bell / <c>mine</c> list deliberately <b>ignores</b> this column — a deferred
    /// notification is visible in-app right away. <c>Notifications.FlushDeferredNotifications</c> sweeps rows
    /// whose instant has passed, delivers the background channels and clears the column back to null.
    /// </para>
    /// <para>
    /// A <c>timestamptz</c> (absolute instant), unlike <see cref="ReadAt"/>: the window it comes from is
    /// evaluated in the deployment's local zone, so the resolved end must survive a DST transition between
    /// send and flush. The decision is <b>frozen at send time</b> — editing or clearing the window afterwards
    /// does not move an already-stamped deferral.
    /// </para>
    /// </summary>
    public DateTimeOffset? DeferredUntil { get; set; }
}