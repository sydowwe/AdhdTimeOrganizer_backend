using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.application.dto.request.@interface;

public interface IMyRequest<TEntity> : ICreateRequest<TEntity>, IUpdateRequest<TEntity>
    where TEntity : class, IEntityWithId
{
}