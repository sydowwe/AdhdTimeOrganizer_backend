namespace MojaDigitalnaFirma.Kernel.notification;

/// <summary>
/// Resolves display text for the loose entity ids a notification payload carries, immediately before the
/// payload is rendered — never before it is persisted.
/// <para>
/// Payloads are **minimized**: they persist ids (<c>employeeId</c>), never person names. A stored name would
/// be frozen PII that survives GDPR erasure forever, because the notification rows belong to the *recipients*
/// (HR / the manager), not to the person named in them — so <c>IEmployeeErasureService</c> never touches them.
/// Resolving the name at render time instead means an anonymized employee degrades on its own, with no
/// backfill or payload scrubber.
/// </para>
/// <para>
/// Implementations enrich a whole batch in one call (one read-seam round trip for all rows), returning a list
/// positionally parallel to the input. Enrichment is best-effort: an implementation that cannot resolve
/// (no ambient request scope, seam failure) returns the payloads unchanged and the renderer falls back to its
/// name-less text.
/// </para>
/// </summary>
public interface INotificationPayloadEnricher
{
    /// <param name="payloadJsons">Persisted payload JSON documents, in order.</param>
    /// <returns>The payloads with display text overlaid, in the same order and count as the input.</returns>
    Task<IReadOnlyList<string>> EnrichAsync(IReadOnlyList<string> payloadJsons, CancellationToken ct = default);
}