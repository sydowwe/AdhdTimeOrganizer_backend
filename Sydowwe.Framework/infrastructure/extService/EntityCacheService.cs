using Microsoft.Extensions.Caching.Distributed;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.extServiceContract;
using Sydowwe.Framework.domain.extServiceContract.user;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.extService;

public class EntityCacheService<T>(IDistributedCache cache, ILoggedUserService loggedUserService) : IEntityCacheService<T> where T : BaseEntity
{
    public string MakeCacheKey(T entity) => $"{entity.GetType().Name}_{entity.Id}_{loggedUserService.GetUserId}";

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