using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseUpdateMapper<TEntity, in TUpdateRequest> : IMapperService
    where TEntity : class, IEntityWithId
    where TUpdateRequest : class, IUpdateRequest
{
    void UpdateEntity(TUpdateRequest request, TEntity entity);
}