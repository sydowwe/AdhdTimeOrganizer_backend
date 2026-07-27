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