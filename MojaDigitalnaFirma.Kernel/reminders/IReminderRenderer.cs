using MojaDigitalnaFirma.Kernel.notification;
using MojaDigitalnaFirma.Kernel.notification.payload;

namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// Optional keyed strategy an <b>owning module</b> may implement when a reminder's text isn't a plain
/// Notification <see cref="NotificationType"/>. Keyed by <see cref="TemplateKey"/>; the Reminders module
/// invokes it at dispatch time only when the registration left <c>NotificationType</c> null (phase 03).
/// <para>
/// <b>Seam:</b> dispatch ultimately calls
/// <c>INotificationService.NotifyAsync(recipients, NotificationType, payload)</c> — there is no transport
/// path for free-form pre-rendered title/body. So a renderer must resolve to a
/// <see cref="RenderedReminder"/> (a <see cref="NotificationType"/> + payload pair) the Notification
/// module can render. A reminder that genuinely needs free-form text is a generic/passthrough
/// <see cref="NotificationType"/> to add in the Notification module — not a new transport path here.
/// </para>
/// </summary>
public interface IReminderRenderer
{
    /// <summary>Stable key matched against <see cref="ReminderRegistration.TemplateKey"/>.</summary>
    string TemplateKey { get; }

    /// <summary>Map the reminder occurrence into the Notification type + payload pair to dispatch.</summary>
    RenderedReminder Render(ReminderRenderContext context);
}

/// <summary>
/// The result of an <see cref="IReminderRenderer"/>: a <see cref="NotificationType"/> the Notification
/// module renders, plus the payload to render it from.
/// </summary>
/// <param name="Type">The Notification type to dispatch.</param>
/// <param name="Payload">
/// The payload the Notification module's text renderer reads from. Constrained to
/// <see cref="INotificationPayload"/> so an owning module's renderer obeys the same payload PII contract as a
/// direct notification producer — read that interface's doc before adding a field.
/// </param>
public sealed record RenderedReminder(NotificationType Type, INotificationPayload? Payload);