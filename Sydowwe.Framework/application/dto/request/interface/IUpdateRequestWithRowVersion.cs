using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.application.dto.request.@interface;

public interface IUpdateRequestWithRowVersion<in TEntity> : IUpdateRequest<TEntity>
    where TEntity : class, IEntityWithId
{
    uint RowVersion { get; }
}