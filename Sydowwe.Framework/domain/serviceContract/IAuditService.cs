namespace Sydowwe.Framework.domain.serviceContract;

public interface IAuditService
{
    Task LogAsync(string eventType, object? payload = null, string? entityName = null, long? entityId = null, CancellationToken ct = default);
    Task LogAsync<TEntity>(string eventType, TEntity entity, object? payload = null, CancellationToken ct = default) where TEntity : class;

    /// <summary>
    /// Queues the business-audit event and immediately persists it. Use from endpoints/services that have no
    /// other tracked change of their own to flush (e.g. a SharePoint upload that returns without touching the
    /// DbContext); the regular <see cref="LogAsync(string, object?, string?, long?, CancellationToken)"/> only
    /// queues the row and relies on a surrounding SaveChanges.
    /// </summary>
    Task LogAndSaveAsync(string eventType, object? payload = null, string? entityName = null, long? entityId = null, CancellationToken ct = default);

    Task LogAndSaveAsync<TEntity>(string eventType, TEntity entity, object? payload = null, CancellationToken ct = default) where TEntity : class;
}