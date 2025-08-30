using AdhdTimeOrganizer.domain.model.entity;

namespace AdhdTimeOrganizer.domain.extServiceContract;

public interface IEntityCacheService<T> where T : BaseEntity
{
    string MakeCacheKey(T entity);
    Task CacheEntityAsync(T entity, string? key = null);
    Task<T?> GetCachedEntityAsync(string key);
    Task RemoveCachedEntityAsync(string key);
}