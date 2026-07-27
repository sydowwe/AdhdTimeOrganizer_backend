using MojaDigitalnaFirma.Kernel.notification;

namespace AdhdTimeOrganizer.Notifications.application;

/// <summary>
/// Fallback <see cref="INotificationPayloadEnricher"/> for a host that ships Notifications <b>without</b> the
/// module owning the referenced entities — payloads render exactly as persisted, so a type carrying only
/// <c>employeeId</c> falls back to the renderer's name-less text.
/// <para>
/// Deliberately <b>not</b> marked <see cref="Sydowwe.Framework.config.dependencyInjection.IScopedService"/>:
/// an auto-registered no-op could win over the real enricher and silently disable name resolution. It is
/// wired via <c>TryAddScoped</c> in the host instead, so a real implementation always takes precedence.
/// </para>
/// </summary>
public sealed class NoOpNotificationPayloadEnricher : INotificationPayloadEnricher
{
    public Task<IReadOnlyList<string>> EnrichAsync(IReadOnlyList<string> payloadJsons, CancellationToken ct = default) => Task.FromResult(payloadJsons);
}