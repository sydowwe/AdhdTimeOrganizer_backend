using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>Enable/disable one (type, channel) pair for the current user.</summary>
public class UpdateNotificationPreferenceRequest
{
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; }
}