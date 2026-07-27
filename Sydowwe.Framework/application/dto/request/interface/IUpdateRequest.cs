using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.application.dto.request.@interface;

public interface IUpdateRequest<in TEntity> where TEntity : class, IEntityWithId
{
    public void UpdateEntity(TEntity entity);
}