namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>
/// Set the caller's quiet-hours window. Minutes are from local midnight (0..1439) in the deployment time
/// zone; <c>Start &gt; End</c> is a valid overnight window, <c>Start == End</c> is rejected — to disable
/// quiet hours call <c>DELETE /notification-quiet-hours</c> rather than encoding an ambiguous zero span.
/// </summary>
public record SetNotificationQuietHoursRequest
{
    public required int StartMinute { get; init; }
    public required int EndMinute { get; init; }
}