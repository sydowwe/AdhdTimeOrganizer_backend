using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.History.application.notification;

/// <summary>
/// The notification kinds the History slice raises, both from <c>TimerBoundaryAlarmJobHandler</c>. Each key is
/// stored and on the wire, and is what the client's <c>notificationTypeMeta</c> is keyed on: <b>only add, never
/// rename</b>. The <c>const</c> beside each typed value is what <c>[NotificationPayload]</c> takes.
/// </summary>
public static class HistoryNotificationTypes
{
    public const string TimerBoundaryKey = "TimerBoundary";
    public const string StopwatchCheckInKey = "StopwatchCheckIn";

    public static readonly NotificationType TimerBoundary = new(TimerBoundaryKey);
    public static readonly NotificationType StopwatchCheckIn = new(StopwatchCheckInKey);
}
