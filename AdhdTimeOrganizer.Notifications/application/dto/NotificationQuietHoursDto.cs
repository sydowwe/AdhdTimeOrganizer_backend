namespace AdhdTimeOrganizer.Notifications.application.dto;

/// <summary>A quiet-hours window: minutes from local midnight (0..1439), <c>Start &gt; End</c> = overnight.</summary>
public record NotificationQuietHoursDto
{
    public required int StartMinute { get; init; }
    public required int EndMinute { get; init; }
}