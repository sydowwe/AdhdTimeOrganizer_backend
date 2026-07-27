using System.Text.Json.Nodes;

namespace MojaDigitalnaFirma.Kernel.notification.payload;

/// <summary>
/// The one sanctioned way to dispatch a payload that is <b>already persisted JSON</b>: rehydration, not
/// authoring. Reminders' dispatch reads <c>ReminderDefinition.PayloadJson</c> back out of the database and
/// forwards it to <see cref="INotificationService"/>, so at that point there is no record to construct — the
/// document exists.
/// <para>
/// This is <b>not</b> an escape hatch from the payload PII contract (see <see cref="INotificationPayload"/>).
/// The contract is enforced where such a document is <i>written</i>: <c>ReminderRegistration.Payload</c> is
/// typed <c>IReminderPayload</c>, so the JSON this wraps provably came from a guarded record. Never construct
/// one from freshly-built content — use the typed record for the notification kind instead.
/// </para>
/// <para><see cref="NotificationService"/> serializes this to <see cref="Json"/> verbatim rather than wrapping it.</para>
/// </summary>
/// <param name="Json">The stored payload document, or null for an empty payload.</param>
public sealed record RawNotificationPayload(JsonNode? Json) : INotificationPayload
{
    /// <summary>Rehydrates a stored payload document; returns null for empty/blank JSON.</summary>
    public static RawNotificationPayload? Parse(string? payloadJson) => string.IsNullOrWhiteSpace(payloadJson) ? null : new RawNotificationPayload(JsonNode.Parse(payloadJson));
}