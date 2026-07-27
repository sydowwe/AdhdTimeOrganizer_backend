using System.Text.Json.Nodes;
using MojaDigitalnaFirma.Kernel.notification.payload;

namespace MojaDigitalnaFirma.Kernel.reminders;

/// <summary>
/// A reminder payload that arrived as a <b>JSON document rather than a typed record</b> — the manual-ops
/// register endpoint, where an admin posts a free-form payload and there is no record to bind it to.
/// <para>
/// This is the one place the payload PII contract cannot be enforced by the compiler, so it is enforced at
/// runtime instead: the endpoint rejects a document carrying a person-data key
/// (<see cref="PayloadPersonDataNames.ContainsPersonData"/>) with a 400 before it ever reaches the registry.
/// Module code registering from its own source must use a typed <see cref="IReminderPayload"/> record —
/// that path keeps the compile-time guarantee.
/// </para>
/// </summary>
/// <param name="Json">The posted payload document, or null when no payload was supplied.</param>
public sealed record RawReminderPayload(JsonNode? Json) : IReminderPayload;