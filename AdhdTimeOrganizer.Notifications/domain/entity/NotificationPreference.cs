using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.entity.user;

namespace AdhdTimeOrganizer.Notifications.domain.entity;

/// <summary>
/// Per-user opt-out for a (type, channel) pair. Absence of a row means "enabled" (default
/// all-on) — only an explicit row with Enabled=false suppresses delivery on that channel.
/// </summary>
[NoAudit]
public class NotificationPreference : BaseTableEntity, IEntityWithUser
{
    /// <summary>Owner. Plain indexed column; the FK is configured host-side (see Notification.UserId).</summary>
    public long UserId { get; set; }

    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; } = true;
}