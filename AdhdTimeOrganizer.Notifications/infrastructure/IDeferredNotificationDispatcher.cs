using AdhdTimeOrganizer.Notifications.domain.entity;

namespace AdhdTimeOrganizer.Notifications.infrastructure;

/// <summary>
/// The module-internal seam the flush job (<c>FlushDeferredNotificationsJobHandler</c>) uses to deliver the
/// background channels of notifications that were withheld during a recipient's quiet hours.
/// <para>
/// Deliberately <b>not</b> in the Kernel: unlike <see cref="MojaDigitalnaFirma.Kernel.notification.INotificationService"/>
/// this is not a capability other modules may call — re-delivering a stored notification is an internal detail
/// of how Notifications honours quiet hours. It exists as an interface only so the job (which owns the query,
/// the batching and the column clear) can reuse <c>NotificationService</c>'s channel machinery — the render →
/// enrich → preference-filter → fan-out stack — without duplicating it.
/// </para>
/// </summary>
public interface IDeferredNotificationDispatcher
{
    /// <summary>
    /// Delivers Web Push + Email for already-persisted notifications, re-checking each recipient's per-channel
    /// preferences at flush time (a channel switched off since the send is honoured). Writes nothing to the
    /// notification rows — clearing <c>DeferredUntil</c> is the caller's job. Best-effort per recipient, like
    /// the live path: an individual transport failure is logged, never thrown.
    /// </summary>
    Task DispatchDeferredAsync(IReadOnlyCollection<Notification> notifications, CancellationToken ct);
}