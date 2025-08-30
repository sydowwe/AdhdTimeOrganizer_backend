using AdhdTimeOrganizer.application.dto.response.@base;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseResponseMapper<in TEntity, out TResponse> : IMapperService
    where TEntity : class, IEntityWithId
    where TResponse : class, IMyResponse
{
    public TResponse ToResponse(TEntity entity);
}