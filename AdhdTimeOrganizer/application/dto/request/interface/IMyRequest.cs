using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.dto.request.@interface;

public interface IMyRequest<TEntity> : ICreateRequest<TEntity>, IUpdateRequest<TEntity>
    where TEntity : class, IEntityWithId
{
}