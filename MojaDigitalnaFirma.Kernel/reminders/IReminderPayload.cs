using MojaDigitalnaFirma.Kernel.notification.payload;

namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// Marker for the payload an owning module stores on a <see cref="ReminderRegistration"/> — the reminders-side
/// half of the payload PII contract. <b>The rule and the reasoning are stated once, on
/// <see cref="INotificationPayload"/>: ids and non-person scalars, never free-text person data.</b> The same
/// reflection guard test walks both markers.
/// <para>
/// A separate marker rather than reusing <see cref="INotificationPayload"/> because this is <i>renderer
/// input</i>, not a rendered notification: a registration whose <c>NotificationType</c> is null resolves its
/// text through a keyed <see cref="IReminderRenderer"/>, which maps this into the notification payload. It is
/// persisted to <c>ReminderDefinition.PayloadJson</c> and rehydrated at dispatch as a
/// <see cref="RawNotificationPayload"/> — which is why constraining it <i>here</i>, at the write, is what
/// makes that rehydration safe.
/// </para>
/// </summary>
public interface IReminderPayload;