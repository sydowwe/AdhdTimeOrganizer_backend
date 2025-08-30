using AdhdTimeOrganizer.application.dto.response.generic;
using AdhdTimeOrganizer.config.dependencyInjection;
using AdhdTimeOrganizer.domain.model.entityInterface;

namespace AdhdTimeOrganizer.application.mapper.@interface;

public interface IBaseSelectOptionMapper<in TEntity> : IMapperService
    where TEntity : class, IEntityWithId
{
    public SelectOptionResponse ToSelectOptionResponse(TEntity entity);
}