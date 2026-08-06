using Microsoft.Extensions.Caching.Distributed;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.extService;

public class CacheService<T>(IDistributedCache cache)
    where T : class
{
    public async Task CacheEntityAsync(string key, T entity)
    {
        var serializedEntity = JsonHelper.Serialize(entity);
        await cache.SetStringAsync(key, serializedEntity);
    }

    public async Task<T?> GetCachedEntityAsync(string key)
    {
        var serializedEntity = await cache.GetStringAsync(key);
        return serializedEntity == null ? null : JsonHelper.Deserialize<T>(serializedEntity);
    }

    public async Task RemoveCachedEntityAsync(string key)
    {
        await cache.RemoveAsync(key);
    }
}