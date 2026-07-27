using MojaDigitalnaFirma.Kernel.notification.payload;

namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// Cross-cutting entry point for raising notifications from any module (background jobs,
/// business events, etc.). The implementation (Core.Notifications) persists a history row,
/// resolves recipients, filters by per-user channel preferences, then dispatches over
/// SignalR (live) and Web Push (background). Safe to call with no authenticated user
/// (e.g. from a Quartz job).
/// <para>
/// Payloads are constrained to <see cref="INotificationPayload"/> — read that contract before adding one.
/// The constraint is the enforcement: there is deliberately no <c>object</c> overload, so a producer cannot
/// hand-roll an anonymous object carrying a person's name.
/// </para>
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Raise a notification, deriving the kind from the payload's <see cref="NotificationPayloadAttribute"/>.
    /// The overload every <b>static</b> producer should use — the type and the payload shape cannot disagree.
    /// </summary>
    Task NotifyAsync<TPayload>(NotificationRecipients recipients, TPayload payload, CancellationToken ct = default)
        where TPayload : INotificationPayload =>
        NotifyAsync(recipients, NotificationPayloadTypes.Of(payload), payload, ct);

    /// <summary>
    /// Raise a notification whose kind is only known at <b>runtime</b> — the Reminders dispatch path, where the
    /// type comes from the stored definition or a keyed <c>IReminderRenderer</c>. Prefer the typed overload
    /// everywhere else.
    /// </summary>
    Task NotifyAsync(NotificationRecipients recipients, NotificationType type, INotificationPayload? payload = null, CancellationToken ct = default);
}