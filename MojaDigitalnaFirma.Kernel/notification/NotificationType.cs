namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// Catalog of notification kinds. Title/body text for each type is rendered at send
/// time from this value + the payload (see INotificationTextRenderer in AdhdTimeOrganizer.Notifications),
/// so the persisted notification stays locale-agnostic. Add new members here as new
/// business events are introduced.
/// </summary>
public enum NotificationType
{
    DeadlineApproaching,
    Test,

    /// <summary>Aggregated reminder digest: N due reminder occurrences batched into one notification (Reminders phase 04b).</summary>
    ReminderDigest,

    /// <summary>
    /// A reminder the user set for themselves — a portal <c>Reminder</c> row, standalone or attached to a
    /// <c>PlannerTask</c>. Deliberately distinct from <see cref="DeadlineApproaching"/>: that one is
    /// email-default-ON, and a self-set nudge the user already sees in-app must not also mail them every
    /// time. Kept out of <c>NotificationChannelDefaults.EmailOnByDefault</c> for exactly that reason.
    /// </summary>
    PersonalReminder,

    // Routines (portal todo-list domain)
    /// <summary>
    /// A <c>RoutineTimePeriod</c> is about to reset with items still unfinished — the lead-time nudge, fired
    /// <c>ReminderLeadDays</c> before the reset. Emitted by a daily sweep rather than a registered reminder on
    /// purpose: the counts in the body ("3 of 8 done") must be live at nudge time, and a reminder payload is
    /// frozen at registration time. Kept out of <c>NotificationChannelDefaults.EmailOnByDefault</c> — same
    /// reasoning as <see cref="PersonalReminder"/>.
    /// </summary>
    RoutinePeriodEndingSoon,

    /// <summary>
    /// A <c>RoutineTimePeriod</c> just reset: how much got done, and what that did to the streak. Reactive, not
    /// scheduled — raised by the reset job at the moment it clears the items, so there is nothing to schedule.
    /// </summary>
    RoutinePeriodEnded,

    /// <summary>
    /// A period's streak grace window (<c>StreakGraceUntil</c>) is about to lapse. Distinct from
    /// <see cref="RoutinePeriodEnded"/> because it fires mid-period, between two resets.
    /// </summary>
    RoutineStreakGraceExpiring,

    // Scheduler
    /// <summary>
    /// A recurring background job failed terminally — its final auto-retry failed, or it is silently
    /// misconfigured (handler not found). Raised via the Scheduler's <c>IJobFailureNotifier</c> seam
    /// (scheduler follow-up 05); payload is PII-free (jobKey / ownerModule / errorType / runId).
    /// </summary>
    ScheduledJobFailed,

    /// <summary>
    /// A recurring background job <b>never fired</b> — it is <c>Active</c> but past its expected run time by
    /// more than the sweep's margin (scheduler follow-up 08). Kept distinct from
    /// <see cref="ScheduledJobFailed"/> on purpose: the failure mode is silence, not an error, and the fix is
    /// usually a registration/boot problem rather than a broken handler — conflating them in one inbox row
    /// makes triage harder. Payload is PII-free (jobKey / ownerModule / expectedRunAt).
    /// </summary>
    ScheduledJobOverdue
}