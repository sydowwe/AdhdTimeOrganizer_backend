using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.persistence.audit;

public class AuditSaveChangesInterceptor(ILoggedUserService loggedUserService, ILogger<AuditSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    private static readonly ConcurrentDictionary<Type, bool> EntityAuditableCache = new();
    private static readonly ConcurrentDictionary<Type, HashSet<string>> IgnoredPropertiesCache = new();

    private static readonly HashSet<string> AlwaysIgnored = new(StringComparer.Ordinal)
    {
        "row_version",
        nameof(BaseTableEntity.CreatedTimestamp),
        nameof(BaseTableEntity.ModifiedTimestamp)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private List<PendingAudit>? _pending;
    private IDbContextTransaction? _ownedTransaction;
    private bool _isSavingAuditLogs;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is null || _isSavingAuditLogs)
            return result;

        _pending = CapturePending(dbContext);

        if (_pending.Count > 0 && dbContext.Database.CurrentTransaction is null)
            _ownedTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is null || _isSavingAuditLogs || _pending is null || _pending.Count == 0)
        {
            if (!_isSavingAuditLogs)
                await DisposeTransactionAsync();
            return result;
        }

        try
        {
            var userId = TryGetUserId();
            var correlationId = Activity.Current?.TraceId.ToString();
            var now = DateTime.UtcNow;
            var today = DateOnly.FromDateTime(now);
            var auditRows = new List<AuditLog>(_pending.Count);

            foreach (var pending in _pending)
            {
                var entityId = pending.Action switch
                {
                    AuditActionEnum.Insert => pending.Entry!.Entity is BaseEntity be ? be.Id : 0L,
                    AuditActionEnum.Update => pending.EntityId,
                    AuditActionEnum.Delete => pending.EntityId,
                    _ => 0L
                };

                var after = pending.Action switch
                {
                    AuditActionEnum.Insert => SerializeCurrentValues(pending.Entry!, pending.IgnoredProps),
                    AuditActionEnum.Update => pending.After,
                    _ => null
                };

                auditRows.Add(new AuditLog
                {
                    EntityName = pending.EntityName,
                    EntityId = entityId,
                    Action = pending.Action,
                    Before = pending.Before,
                    After = after,
                    ChangedProperties = pending.ChangedProperties,
                    UserId = userId,
                    CorrelationId = correlationId,
                    Timestamp = now,
                    Date = today
                });
            }

            _isSavingAuditLogs = true;
            await dbContext.Set<AuditLog>().AddRangeAsync(auditRows, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (_ownedTransaction is not null)
                await _ownedTransaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write audit log entries");
            if (_ownedTransaction is not null)
                await _ownedTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            _isSavingAuditLogs = false;
            await DisposeTransactionAsync();
            _pending = null;
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _ownedTransaction?.Rollback();
        _ownedTransaction?.Dispose();
        _ownedTransaction = null;
        _pending = null;
        _isSavingAuditLogs = false;
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (_ownedTransaction is not null)
        {
            await _ownedTransaction.RollbackAsync(cancellationToken);
            await _ownedTransaction.DisposeAsync();
            _ownedTransaction = null;
        }

        _pending = null;
        _isSavingAuditLogs = false;
    }

    private async Task DisposeTransactionAsync()
    {
        if (_ownedTransaction is not null)
        {
            await _ownedTransaction.DisposeAsync();
            _ownedTransaction = null;
        }
    }

    private static List<PendingAudit> CapturePending(DbContext dbContext)
    {
        var pending = new List<PendingAudit>();

        foreach (var entry in dbContext.ChangeTracker.Entries<BaseTableEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var entityType = entry.Entity.GetType();
            if (!IsAuditable(entityType))
                continue;

            var ignoredProps = GetIgnoredProperties(entityType);
            var entityName = entityType.Name;

            switch (entry.State)
            {
                case EntityState.Added:
                    pending.Add(new PendingAudit
                    {
                        Entry = entry,
                        EntityName = entityName,
                        Action = AuditActionEnum.Insert,
                        IgnoredProps = ignoredProps
                    });
                    break;

                case EntityState.Modified:
                    var changed = entry.Properties
                        .Where(p => p.IsModified && !ignoredProps.Contains(p.Metadata.Name) && !AlwaysIgnored.Contains(p.Metadata.Name))
                        .Select(p => p.Metadata.Name)
                        .ToArray();
                    if (changed.Length == 0)
                        break;
                    pending.Add(new PendingAudit
                    {
                        Entry = entry,
                        EntityName = entityName,
                        EntityId = entry.Entity.Id,
                        Action = AuditActionEnum.Update,
                        Before = SerializeOriginalValues(entry, ignoredProps),
                        After = SerializeCurrentValues(entry, ignoredProps),
                        ChangedProperties = changed,
                        IgnoredProps = ignoredProps
                    });
                    break;

                case EntityState.Deleted:
                    pending.Add(new PendingAudit
                    {
                        EntityName = entityName,
                        EntityId = entry.Entity.Id,
                        Action = AuditActionEnum.Delete,
                        Before = SerializeOriginalValues(entry, ignoredProps),
                        IgnoredProps = ignoredProps
                    });
                    break;
            }
        }

        return pending;
    }

    private static bool IsAuditable(Type entityType)
    {
        return EntityAuditableCache.GetOrAdd(entityType, static t =>
            !Attribute.IsDefined(t, typeof(NoAuditAttribute))
            && typeof(BaseTableEntity).IsAssignableFrom(t)
            && t != typeof(AuditLog)
            && t != typeof(BusinessAuditLog));
    }

    private static HashSet<string> GetIgnoredProperties(Type entityType)
    {
        return IgnoredPropertiesCache.GetOrAdd(entityType, static t =>
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in t.GetProperties())
                if (Attribute.IsDefined(prop, typeof(AuditIgnoreAttribute)))
                    set.Add(prop.Name);
            return set;
        });
    }

    private static string SerializeOriginalValues(EntityEntry entry, HashSet<string> ignored) => SerializeValues(entry.OriginalValues, ignored);

    private static string SerializeCurrentValues(EntityEntry entry, HashSet<string> ignored) => SerializeValues(entry.CurrentValues, ignored);

    private static string SerializeValues(PropertyValues values, HashSet<string> ignored)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in values.Properties)
        {
            if (ignored.Contains(prop.Name) || AlwaysIgnored.Contains(prop.Name))
                continue;
            dict[prop.Name] = values[prop.Name];
        }

        return JsonHelper.Serialize(dict);
    }

    private long? TryGetUserId()
    {
        try
        {
            return loggedUserService.IsAuthenticated ? loggedUserService.GetUserId : null;
        }
        catch (Exception ex)
        {
            logger.LogDebug("No logged user for audit row: {message}", ex.Message);
            return null;
        }
    }

    private sealed class PendingAudit
    {
        public EntityEntry? Entry { get; init; }
        public string EntityName { get; init; } = null!;
        public long EntityId { get; init; }
        public AuditActionEnum Action { get; init; }
        public string? Before { get; init; }
        public string? After { get; init; }
        public string[]? ChangedProperties { get; init; }
        public HashSet<string> IgnoredProps { get; init; } = new();
    }
}