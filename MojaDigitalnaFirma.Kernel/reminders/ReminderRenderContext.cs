using System.Text.Json.Nodes;

namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// The context handed to an <see cref="IReminderRenderer"/> at dispatch time. Same shape as
/// <see cref="ReminderResolutionContext"/> but for the text path: it lets the owner's renderer map the
/// reminder into the <see cref="RenderedReminder"/> pair the Notification module can render.
/// </summary>
/// <param name="Key">The reminder's idempotency key.</param>
/// <param name="TemplateKey">The reminder's content key.</param>
/// <param name="Payload">
/// The reminder's payload, <b>rehydrated</b> from the persisted <c>jsonb</c> document — which is why it is
/// a <see cref="JsonNode"/> and not an <see cref="IReminderPayload"/>: by dispatch time the typed record the
/// owner registered no longer exists, only the JSON it serialised to. The payload PII contract is enforced
/// where the document is <i>written</i> (<see cref="ReminderRegistration.Payload"/>), not here.
/// </param>
/// <param name="OccurrenceAt">The instant of the occurrence being dispatched.</param>
public sealed record ReminderRenderContext(ReminderKey Key, string TemplateKey, JsonNode? Payload, DateTimeOffset OccurrenceAt);