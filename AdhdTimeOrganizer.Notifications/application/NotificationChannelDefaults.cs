using MojaDigitalnaFirma.Kernel.notification;

namespace AdhdTimeOrganizer.Notifications.application;

/// <summary>
/// What a channel does for a given type when the user has no explicit <c>NotificationPreference</c> row.
/// <para>
/// InApp and WebPush stay default-on for everything — they are cheap and in-context. Email is not:
/// a mailbox is a shared, permanent, noisy surface, so only types that genuinely need to reach someone
/// who has the app closed (deadlines, job alerts) default on. Digest/noise types default off
/// and the user must opt in. Unknown/future types default off for email — the safe direction.
/// </para>
/// <para>
/// LEGAL: this default-on set is lawful only because every member is an <b>operational</b> notification
/// about the recipient's own work. Marketing content must be opt-IN (consent) per §116 zák. 452/2021 —
/// never add a marketing type here.
/// </para>
/// </summary>
public static class NotificationChannelDefaults
{
    private static readonly HashSet<NotificationType> EmailOnByDefault =
    [
        NotificationType.DeadlineApproaching,
        // A background job failing is operational and time-sensitive: the admin who must act may well have the
        // app closed, so email default-on is the point of the alert. Not marketing — lawful per the note above.
        NotificationType.ScheduledJobFailed,
        // Same reasoning, more so: an overdue job is failing SILENTLY, so an in-app badge nobody opens is
        // exactly the surface that would miss it. Safe to default on only because its throttle window is
        // 12 hours (OverdueJobSweepOptions.AlertThrottleHours) — an overdue condition persists continuously
        // until fixed, so at the failure throttle's 1 hour this would be 24 mails a day. If that window is
        // ever shortened, revisit this line.
        NotificationType.ScheduledJobOverdue
        // NOT NotificationType.PersonalReminder, deliberately. It is a self-set nudge the user already sees
        // in-app and on the device; mailing every "call the dentist at 15:00" would make the inbox the noisy
        // surface this table exists to protect. The user can still opt in with a NotificationPreference row.
        //
        // NOT the three Routine* types either, for the same reason and more so: they recur on every period of
        // every routine the user keeps, so email-default-on would be the single noisiest thing in this table.
        // They are opt-in per period already (RoutineTimePeriod.ReminderLeadDays), and a user who does want
        // them mailed opts in per channel like any other type.
    ];

    /// <summary>True when the channel fires for this type without an explicit preference row.</summary>
    public static bool IsEnabledByDefault(NotificationType type, NotificationChannel channel) => channel != NotificationChannel.Email || EmailOnByDefault.Contains(type);
}