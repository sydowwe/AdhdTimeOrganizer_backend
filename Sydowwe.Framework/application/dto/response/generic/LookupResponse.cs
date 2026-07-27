using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.application.dto.response.generic;

/// <summary>
/// Read projection for any lookup entity, user-scoped or not — hence the
/// <see cref="IBaseLookupEntity"/> constraint rather than either concrete base.
/// </summary>
public record LookupResponse<TEntity> : SelectOptionResponse, IProjectionResponse<LookupResponse<TEntity>, TEntity>
    where TEntity : class, IBaseLookupEntity
{
    public int? SortOrder { get; init; }

    public static IQueryable<LookupResponse<TEntity>> Projection(IQueryable<TEntity> query)
    {
        return query.Select(entity => new LookupResponse<TEntity>
        {
            Id = entity.Id,
            Text = entity.Text,
            SortOrder = entity.SortOrder
        });
    }
}
