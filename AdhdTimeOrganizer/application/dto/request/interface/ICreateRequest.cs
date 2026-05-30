using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.dto.request.@interface;

public interface ICreateRequest
{
}

public interface ICreateRequest<out TEntity> : ICreateRequest where TEntity : class, IEntityWithId
{
    public TEntity ToEntity { get; }
}