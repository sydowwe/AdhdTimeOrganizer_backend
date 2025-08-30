using AdhdTimeOrganizer.domain.extServiceContract;
using AdhdTimeOrganizer.domain.extServiceContract.user;
using AdhdTimeOrganizer.domain.model.entity;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;

namespace AdhdTimeOrganizer.infrastructure.extService;

public class EntityCacheService<T>(IDistributedCache cache, ILoggedUserService loggedUserService) : IEntityCacheService<T> where T : BaseEntity
{
    public string MakeCacheKey(T entity) => $"{entity.GetType().Name}_{entity.Id}_{loggedUserService.GetUserId}";

    public async Task CacheEntityAsync(T entity, string? key = null)
    {
        key ??= MakeCacheKey(entity);
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