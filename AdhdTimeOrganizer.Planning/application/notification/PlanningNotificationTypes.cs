using Sydowwe.Framework.Contracts.notification;

namespace AdhdTimeOrganizer.Planning.application.notification;

/// <summary>
/// The notification kinds the Planning slice raises: the two reminders it registers through the Reminders module
/// (<c>ReminderRegistrationService</c>) and the slip sweep's two nudges (<c>PlannerNotificationService</c>). Each key
/// is stored and on the wire, and is what the client's <c>notificationTypeMeta</c> is keyed on: <b>only add, never
/// rename</b>. The <c>const</c> beside each typed value is what <c>[NotificationPayload]</c> and a <c>switch</c> take.
/// </summary>
public static class PlanningNotificationTypes
{
    public const string PersonalReminderKey = "PersonalReminder";
    public const string PlannedBlockStartingKey = "PlannedBlockStarting";
    public const string PlannedBlocksMissedKey = "PlannedBlocksMissed";
    public const string PlannedBlockOverrunKey = "PlannedBlockOverrun";

    public static readonly NotificationType PersonalReminder = new(PersonalReminderKey);
    public static readonly NotificationType PlannedBlockStarting = new(PlannedBlockStartingKey);
    public static readonly NotificationType PlannedBlocksMissed = new(PlannedBlocksMissedKey);
    public static readonly NotificationType PlannedBlockOverrun = new(PlannedBlockOverrunKey);
}
