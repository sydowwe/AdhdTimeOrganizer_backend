using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Notifications.application;

/// <summary>
/// Turns a notification type + JSON payload into display title/body. Centralizes text so
/// the persisted notification stays locale-agnostic. v1 returns sk-SK strings; swap the
/// implementation for IStringLocalizer-backed, per-recipient-locale rendering when needed.
/// </summary>
public interface INotificationTextRenderer
{
    (string Title, string Body) Render(NotificationType type, string payloadJson);
}