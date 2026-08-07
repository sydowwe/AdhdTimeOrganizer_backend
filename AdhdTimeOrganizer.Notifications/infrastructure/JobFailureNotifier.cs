using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.Contracts.notification;
using Sydowwe.Framework.Contracts.notification.payload;
using Sydowwe.Framework.Contracts.scheduling;
using Sydowwe.Framework.domain.@enum;

namespace AdhdTimeOrganizer.Notifications.infrastructure;

/// <summary>
/// Live consumer of the Scheduler's <see cref="IJobFailureNotifier"/> seam (scheduler follow-up 05): turns a
/// PII-free <see cref="JobFailureAlert"/> into a real in-app + Web Push (+ email, default-on) notification
/// delivered to every <see cref="UserRoleEnum.Admin"/> and <see cref="UserRoleEnum.Root"/> through the
/// existing <see cref="INotificationService"/> pipeline. Auto-registered by the <c>IScopedService</c> scan, so
/// wherever Notifications is deployed this replaces the <c>NoOpJobFailureNotifier</c> fallback and the seam is
/// plugged end-to-end. Safe with no authenticated user (called from the background dispatcher).
/// <para>
/// <b>Two kinds, two notification types</b> (follow-up 08): a job that ran and failed becomes
/// <see cref="NotificationType.ScheduledJobFailed"/>; a job that never fired becomes
/// <see cref="NotificationType.ScheduledJobOverdue"/>. They are split at the payload because the failure modes
/// are opposites — an error versus silence — and an operator triages them differently. The seam itself is
/// shared, so Core.Scheduler still knows nothing about this module.
/// </para>
/// <para>
/// Either payload carries only the alert's non-PII fields — the renderer shows the job key (a technical
/// identifier) and the row belongs to its Admin recipients, so it survives no personal-data lifecycle.
/// </para>
/// </summary>
public sealed class JobFailureNotifier(INotificationService notifications) : IJobFailureNotifier, IScopedService
{
    public Task NotifyJobFailedAsync(JobFailureAlert alert, CancellationToken ct = default)
    {
        // Branch on the explicit kind, never on `RunId is null` — see JobAlertKind for why.
        INotificationPayload payload = alert.Kind switch
        {
            // An overdue alert has no RunId to deep-link (no run row exists), so it carries the fire it
            // MISSED instead — the number that tells an admin how long the job has been silent.
            JobAlertKind.Overdue =>
                new ScheduledJobOverduePayload(alert.JobKey, alert.OwnerModule, alert.ExpectedRunAtUtc),
            _ => new ScheduledJobFailedPayload(alert.JobKey, alert.OwnerModule, alert.ErrorType, alert.RunId)
        };

        return notifications.NotifyAsync(
            NotificationRecipients.InRoles(UserRoleEnum.Admin, UserRoleEnum.Root), payload, ct);
    }
}