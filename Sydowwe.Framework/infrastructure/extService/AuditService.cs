using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.config.dependencyInjection;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.helper;
using Sydowwe.Framework.domain.serviceContract;

namespace Sydowwe.Framework.infrastructure.extService;

public class AuditService(DbContext dbContext, ILoggedUserService loggedUserService, ILogger<AuditService> logger) : IAuditService, IScopedService
{
    public Task LogAsync(string eventType, object? payload = null, string? entityName = null, long? entityId = null, CancellationToken ct = default)
    {
        var entry = new BusinessAuditLog
        {
            EventType = eventType,
            Payload = payload is null ? null : JsonHelper.Serialize(payload),
            EntityName = entityName,
            EntityId = entityId,
            UserId = TryGetUserId(),
            CorrelationId = Activity.Current?.TraceId.ToString(),
            Timestamp = DateTime.UtcNow
        };
        return dbContext.Set<BusinessAuditLog>().AddAsync(entry, ct).AsTask();
    }

    public Task LogAsync<TEntity>(string eventType, TEntity entity, object? payload = null, CancellationToken ct = default) where TEntity : class
    {
        long? id = entity is IEntityWithId withId ? withId.Id : null;
        return LogAsync(eventType, payload, entity.GetType().Name, id, ct);
    }

    public async Task LogAndSaveAsync(string eventType, object? payload = null, string? entityName = null, long? entityId = null, CancellationToken ct = default)
    {
        await LogAsync(eventType, payload, entityName, entityId, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public Task LogAndSaveAsync<TEntity>(string eventType, TEntity entity, object? payload = null, CancellationToken ct = default) where TEntity : class
    {
        long? id = entity is IEntityWithId withId ? withId.Id : null;
        return LogAndSaveAsync(eventType, payload, entity.GetType().Name, id, ct);
    }

    private long? TryGetUserId()
    {
        try
        {
            return loggedUserService.IsAuthenticated ? loggedUserService.GetUserId : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug("No logged user for business audit row: {message}", ex.Message);
            return null;
        }
    }
}