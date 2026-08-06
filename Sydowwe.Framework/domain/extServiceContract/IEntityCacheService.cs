using Sydowwe.Framework.domain.entity.@base;

namespace Sydowwe.Framework.domain.extServiceContract;

public interface IEntityCacheService<T> where T : BaseEntity
{
    string MakeCacheKey(T entity);
    Task CacheEntityAsync(string key, T entity);
    Task<T?> GetCachedEntityAsync(string key);
    Task RemoveCachedEntityAsync(string key);
}