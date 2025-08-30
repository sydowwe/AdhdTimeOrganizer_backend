using AdhdTimeOrganizer.application.dto.request.@interface;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseCreateMapper<TEntity, in TCreateRequest> : IMapperService
    where TEntity : class, IEntityWithId
    where TCreateRequest : class, ICreateRequest
{
    TEntity ToEntity(TCreateRequest request);
}