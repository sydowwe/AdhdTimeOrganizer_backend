namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>
/// The caller's quiet-hours window (see <c>NotificationQuietHours</c>). <see cref="QuietHours"/> is null when
/// none is set — absence means no quiet hours, not an all-day window.
/// </summary>
public record MyNotificationQuietHoursResponse
{
    public NotificationQuietHoursDto? QuietHours { get; init; }
}