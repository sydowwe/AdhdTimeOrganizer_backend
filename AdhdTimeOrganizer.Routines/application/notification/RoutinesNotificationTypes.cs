using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Routines.application.notification;

/// <summary>
/// The notification kinds the Routines slice raises, all through <c>RoutinePeriodNotificationService</c>. Each key is
/// stored and on the wire, and is what the client's <c>notificationTypeMeta</c> is keyed on: <b>only add, never
/// rename</b>. The <c>const</c> beside each typed value is what <c>[NotificationPayload]</c> takes.
/// </summary>
public static class RoutinesNotificationTypes
{
    public const string RoutinePeriodEndingSoonKey = "RoutinePeriodEndingSoon";
    public const string RoutinePeriodEndedKey = "RoutinePeriodEnded";
    public const string RoutineStreakGraceExpiringKey = "RoutineStreakGraceExpiring";
    public const string RoutineSuggestedTodayKey = "RoutineSuggestedToday";

    public static readonly NotificationType RoutinePeriodEndingSoon = new(RoutinePeriodEndingSoonKey);
    public static readonly NotificationType RoutinePeriodEnded = new(RoutinePeriodEndedKey);
    public static readonly NotificationType RoutineStreakGraceExpiring = new(RoutineStreakGraceExpiringKey);
    public static readonly NotificationType RoutineSuggestedToday = new(RoutineSuggestedTodayKey);
}
