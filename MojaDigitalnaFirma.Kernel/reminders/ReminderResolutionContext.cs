using System.Text.Json.Nodes;

namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// The context handed to an <see cref="IReminderRecipientResolver"/> at dispatch time. Carries the
/// reminder's identity, content key, payload and the occurrence instant being dispatched, so the owner's
/// strategy can resolve recipients from its own domain (this module never encodes the rule).
/// </summary>
/// <param name="Key">The reminder's idempotency key.</param>
/// <param name="TemplateKey">The reminder's content key.</param>
/// <param name="Payload">
/// The reminder's payload, rehydrated from the persisted <c>jsonb</c> document — see
/// <see cref="ReminderRenderContext.Payload"/> for why this is a <see cref="JsonNode"/> rather than the
/// typed <see cref="IReminderPayload"/> the owner registered.
/// </param>
/// <param name="OccurrenceAt">The instant of the occurrence being dispatched.</param>
public sealed record ReminderResolutionContext(ReminderKey Key, string TemplateKey, JsonNode? Payload, DateTimeOffset OccurrenceAt);