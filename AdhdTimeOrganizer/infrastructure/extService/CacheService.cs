using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;

namespace AdhdTimeOrganizer.infrastructure.extService;

public class CacheService<T>(IDistributedCache cache)
where T : class
{
    public async Task CacheEntityAsync(string key, T entity)
    {
        var serializedEntity = JsonConvert.SerializeObject(entity);
        await cache.SetStringAsync(key, serializedEntity);
    }

    public async Task<T?> GetCachedEntityAsync(string key)
    {
        var serializedEntity = await cache.GetStringAsync(key);
        return serializedEntity == null ? null : JsonConvert.DeserializeObject<T>(serializedEntity);
    }

    public async Task RemoveCachedEntityAsync(string key)
    {
        await cache.RemoveAsync(key);
    }
}