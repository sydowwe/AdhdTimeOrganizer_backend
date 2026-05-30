using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.dto.request.@interface;

public interface IUpdateRequest { }

public interface IUpdateRequest<in TEntity> : IUpdateRequest where TEntity : class, IEntityWithId
{
    public void UpdateEntity(TEntity entity);
}