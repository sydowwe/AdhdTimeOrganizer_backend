using MojaDigitalnaFirma.Kernel.notification;

namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>
/// Rendered notification sent to clients (SignalR live push and the bell list).
/// Title/Body are produced from Type + payload at send/read time.
/// </summary>
public record NotificationDto(
    long Id,
    NotificationType Type,
    string Title,
    string Body,
    DateTime CreatedAt,
    bool IsRead);